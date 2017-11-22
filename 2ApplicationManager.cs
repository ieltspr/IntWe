using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Teg.Its.Metering.Core;
using Teg.Its.Metering.Core.Extensions;
using Wec.Its.Metering.LGNonIntervalLoadCommon;
using Wec.Its.Metering.LGNonIntervalLoadCommon.Enum;
using Wec.Its.Metering.LGNonIntervalLoadCommon.Dto;
using IecCimFileFormat;
using Wec.Its.Metering.LGNonIntervalLoadDomain.DAL;

namespace Wec.Its.Metering.LGNonIntervalLoadDomain.Manager
{

    /// <summary>
    /// Class which is the entry point into application logic
    /// </summary>
    public class ApplicationManager
    {
        /// <summary>
        /// Holds the reading type cache for current run
        /// </summary>
        public List<AMIReadType> ReadTypeCache { get; set; }

        /// <summary>
        /// Holds the quality type cache for current run
        /// </summary>
        public List<AMIQualityType> QualityTypeCache { get; set; }

        /// <summary>
        /// Holds the meter info cache for current run
        /// </summary>
        public List<Meter> MeterInfoCache { get; set; }

        /// <summary>
        /// Holds the non exception errors that occured for current run
        /// </summary>
        public ConcurrentBag<NonIntervalLoadError> Errors { get; set; }

        #region Constructor
        /// <summary>
        /// Default constructor
        /// </summary>
        public ApplicationManager()
        {
            ReadTypeCache = new List<AMIReadType>();
            QualityTypeCache = new List<AMIQualityType>();
            MeterInfoCache = new List<Meter>();
            Errors = new ConcurrentBag<NonIntervalLoadError>();
        }
        #endregion


        /// <summary>
        /// Entrypoint into the application managers
        /// </summary>
        public void RunApplication()
        {
            try
            {
                Stopwatch sw = new Stopwatch();
                sw.Start();

                Logger.Write(string.Format("Start {0}", System.AppDomain.CurrentDomain.FriendlyName));
                Logger.Write(string.Format("{0}Command line options:{1}{2}", Environment.NewLine, Environment.NewLine, new CommandLineOptions().PropertiesToString()));
                //Logger.Write(string.Format("{0}Application settings:{1}{2}", Environment.NewLine, Environment.NewLine, new AppConfigSettings().PropertiesToString()));

                //Init load type info
                InitializeLoadTypeInfo(CommandLineOptions.LoadType);

                /// Load Quality and Read types into memory (based on AMISystemId = 10)
                CacheQualityandReadTypes();

                /// Load existing devices into memory based on Load Type from the input file name)
                CacheExistingDevices();

                /// Get the cached Meter count 
                decimal expectedDevices = MeterInfoCache.Count();

                /// Get the list of available input files
                decimal processedDevices = ProcessInputFiles();

                //Initialize report manager and call create report
                ErrorReportManager errorRpt = new ErrorReportManager();
                errorRpt.CreateReport(Errors, expectedDevices, processedDevices);

                sw.Stop();
                Logger.Write(string.Format("Application run time: {0}", sw.Elapsed.ToString()));
                //Logger.Write("Stop");
                Logger.Write(string.Format("Stop {0}{1}", Environment.NewLine, Environment.NewLine));
            }
            catch (Exception ex)
            {
                if (AppConfigSettings.PageOnError)
                    Logger.Write(ex.ToString(), TraceEventType.Critical);
                else
                    Logger.Write(ex.ToString(), TraceEventType.Error);
            }
        }

        /// <summary>
        /// Processing through the file(s)
        /// </summary>
        public int ProcessInputFiles()
        {
            DirectoryInfo di = new DirectoryInfo(AppConfigSettings.InputFilePathRoot);
            List<FileInfo> fileInfos = di.GetFiles(LoadTypeInfo.FileMask).ToList();
            int totalProcessedDevices = 0;

            Logger.Write(string.Format("Looking for files in directory {0} using file mask {1}.", di.FullName, LoadTypeInfo.FileMask));

            if (fileInfos.Count == 0)
            {
                if (CommandLineOptions.ModeType == ModeType.Auto)
                {
                    throw new Exception("No reading files found.");
                }
                else
                {
                    Logger.Write("No reading files found.");
                    return totalProcessedDevices;
                }
            }

            Logger.Write(string.Format("Found {0} files.", fileInfos.Count.ToString()));

            foreach (FileInfo file in fileInfos)
            {
                int processedDevices = 0;
                Logger.Write(string.Format("Processing file {0}...", file.Name));

                using (IecCimFileReader reader = new IecCimFileReader(file.FullName))
                {
                    HeaderRecord headerRecord = reader.GetHeaderRecord();
                    TrailerRecord trailerRecord = reader.GetTrailerRecord();

                    if (headerRecord == null || trailerRecord == null)
                        throw new Exception("Header or Trailer record null.");

                    if (trailerRecord.TotalRecordCount != reader.GetDetailRecordCount())
                        Logger.Write("Trailer detail record count does not match the actual # of detail records found in the input file.", TraceEventType.Warning);

                    while (!reader.EndOfStream)
                    {
                        //IList<DetailRecord> records = reader.GetDetailRecordByDevice();
                        //List<Reading> readings = ProcessDetailRecords(records);
                        //InsertReadings(readings);
                        //processedDevices++;
                        //totalProcessedDevices++;

                        List<List<DetailRecord>> recordsToProcess = new List<List<DetailRecord>>();

                        for (Int32 count = 0; count < AppConfigSettings.NumberOfDevicesToThread; count++)
                        {
                            IList<DetailRecord> records = reader.GetDetailRecordByDevice();

                            if (records.Count > 0)
                            {
                                recordsToProcess.Add(records.ToList());
                                processedDevices++;
                                totalProcessedDevices++;
                            }
                        }

                        ParallelOptions options = new ParallelOptions();
                        options.MaxDegreeOfParallelism = CommandLineOptions.NumOfThreads;
                        Parallel.ForEach(recordsToProcess, options,
                            (record) =>
                            {
                                List<Reading> readings = ProcessDetailRecords(record);
                                InsertReadings(readings);
                            });
                    }
                }

                Logger.Write(string.Format("Processed {0} devices in file {1}.", processedDevices.ToString(), file.Name));

                //Move file to completed folder
                string backupInputFileName = @"\LGNonIntervalLoadInputFile-" + DateTime.Now.ToString("MMddyyyyhhmmss") + "-" + file.Name;
                //file.MoveTo(AppConfigSettings.CompletedFilePathRoot + backupInputFileName);
                Logger.Write("Backup of input file created and placed in backup folder.");
            }

            return totalProcessedDevices;
        }

        /// <summary>
        /// Processes the passed detail records and returns readings that are ready to be inserted
        /// </summary>
        /// <param name="records">Detail records to process</param>
        public List<Reading> ProcessDetailRecords(IList<DetailRecord> records)
        {
            if (records == null)
                throw new ArgumentNullException("records");

            List<Reading> readings = new List<Reading>();
            Reading lastValidOnOffReading = null;

            foreach (DetailRecord record in records)
            {
                bool isDetailRecordValid = DetailRecordIsValid(record);
                if (isDetailRecordValid == true)
                {
                    Meter meterInfo = GetMeterInfo(record);
                    if (meterInfo == null)
                        return readings;

                    AMIQualityType qualityType = GetAMIQualityType(record);
                    AMIReadType readType = GetAMIReadType(record, meterInfo);

                    if (isDetailRecordValid && meterInfo != null && qualityType != null && readType != null)
                    {
                        Reading reading = new Reading();
                        reading.MeterInfoId = meterInfo.MeterInfoId;
                        reading.AMIReadTypeId = readType.AMIReadTypeId;
                        reading.AMIQualityTypeId = qualityType.AMIQualityTypeId;
                        reading.ReadDateTime = record.ReadingDateTime.Value;
                        reading.Reading1 = record.ReadingValue.Value;

                        //IF PROCESSING A DEMAND RDG AND THE PREVIOUS RDG WAS A PEAK RDG ... 
                        if (AppConfigSettings.DemandOnOffReadTypeId.Contains(reading.AMIReadTypeId) && lastValidOnOffReading != null)
                        {
                            //THIS DEMAND RECORD'S TAKEN DATE NEEDS TO BE SENT TO THE READDATETIME TABLE,
                            ReadingDateTime readingDateTime = new ReadingDateTime();
                            readingDateTime.ReadDateTime = record.ReadingDateTime.Value;

                            //AND IT'S TAKEN DATE MUST BE OVERWRITTEN BY THE PRIOR PEAK RECORD'S TAKEN DATE.
                            reading.ReadingDateTime = readingDateTime;
                            reading.ReadDateTime = lastValidOnOffReading.ReadDateTime;
                        }

                        //IF NOT PROCESSING A DEMAND RDG OR 
                        //       (PROCESSING A DEMAND RDG AND THE PREVIOUS RDG WAS A PEAK RDG)
                        //THEN ADD THE RDG TO THE RECORD SET
                        if (!AppConfigSettings.DemandOnOffReadTypeId.Contains(reading.AMIReadTypeId) ||
                            (AppConfigSettings.DemandOnOffReadTypeId.Contains(reading.AMIReadTypeId) && lastValidOnOffReading != null))
                            readings.Add(reading);
                        else
                        {
                            string demandrdgError = string.Format("No matching Peak reading Taken date for Demand.     Record:  {0}", record.IecCimRecord);
                            Logger.Write(demandrdgError);
                            Errors.Add(new NonIntervalLoadError(record.MeterId, record.ServicePointId, demandrdgError));
                        }

                        //IF PROCESSING A PEAK RDG, SAVE IT OFF TO THE SIDE, NEXT RECORD SHOULD BE THE DEMAND RDG ...
                        if (AppConfigSettings.OnOffReadTypeId.Contains(reading.AMIReadTypeId))
                            lastValidOnOffReading = reading;
                        else
                            lastValidOnOffReading = null;
                    }
                }
            }

            return readings;
        }

        /// <summary>
        /// Inserts the passed readings into the database if reading doesn't already exist
        /// </summary>
        /// <param name="readings">Readings to insert</param>
        public void InsertReadings(List<Reading> readings)
        {
            if (readings == null)
                throw new ArgumentNullException("readings");

            if(readings.Count == 0)
            {
                Logger.Write("No readings to insert.");
                return;
            }

            using (UocEntities context = new UocEntities())
            {
                LookupQuery lookupQuery = new LookupQuery(context);

                foreach (Reading reading in readings)
                {
                    if (lookupQuery.GetReading(reading).Count == 0)
                    {
                        context.Readings.Add(reading);
                    }
                    else
                    {
                        string duplicateError = string.Format("Reading is duplicate and will not be inserted.  MeterInfoId:{0} AMIReadTypeId:{1} AMIQualityTYpeId:{2} ReadDateTime:{3}", reading.MeterInfoId, reading.AMIReadTypeId, reading.AMIQualityTypeId, reading.ReadDateTime);
                        Logger.Write(duplicateError);
                    }
                }

                context.SaveChanges();
            }
        }

        /// <summary>
        /// Validates the passed detail record
        /// </summary>
        /// <param name="record">The detail record to validate</param>
        /// <returns>True if valid false if not valid</returns>
        public bool DetailRecordIsValid(DetailRecord record)
        {
            if (record == null)
                throw new ArgumentNullException("record");

            if (!record.ReadingDateTime.HasValue)
            {
                string readingDateTimeError = string.Format("ReadingDateTime is null.     Record:  {0}", record.IecCimRecord);
                Logger.Write(readingDateTimeError);
                Errors.Add(new NonIntervalLoadError(record.MeterId, record.ServicePointId, readingDateTimeError));
                return false;
            }

            if (!record.ReadingValue.HasValue)
            {
                string readingValueError = string.Format("ReadingValue is null.     Record:  {0}", record.IecCimRecord);
                Logger.Write(readingValueError);
                Errors.Add(new NonIntervalLoadError(record.MeterId, record.ServicePointId, readingValueError));
                return false;
            }

            if(record.ServicePointId.Length != 12)
            {
                string servicePointError = string.Format("Premise is not length 12.     Record:  {0}", record.IecCimRecord);
                Logger.Write(servicePointError);
                Errors.Add(new NonIntervalLoadError(record.MeterId, record.ServicePointId, servicePointError));
                return false;
            }

            Int64 outSPId;
            if (!Int64.TryParse(record.ServicePointId, out outSPId))
            {
                string servicePointError = string.Format("Premise is not an integer.     Record:  {0}", record.IecCimRecord);
                Logger.Write(servicePointError);
                Errors.Add(new NonIntervalLoadError(record.MeterId, record.ServicePointId, servicePointError));
                return false;
            }

            return true;
        }

        /// <summary>
        /// Retrieves the meter info which matches the passed detail record
        /// </summary>
        /// <param name="record">Detail record to find the matching meter info for</param>
        /// <returns>MeterInfo object if found null if not found</returns>
        public Meter GetMeterInfo(DetailRecord record)
        {
            if (record == null)
                throw new ArgumentNullException("record");

            List<Meter> meterInfo = MeterInfoCache.Where
            (m =>
                m.MeterNumber == record.MeterId
                && m.PremiseId == Convert.ToInt32(record.ServicePointId.Substring(0, 9))
                && m.PremiseServiceSequence == Convert.ToInt16(record.ServicePointId.Substring(9, 3))
            ).ToList();

            if (meterInfo.Count == 0)
            {
                string meterInfoError = string.Format("MeterInfo not found.     Record:  {0}", record.IecCimRecord);
                Logger.Write(meterInfoError);
                Errors.Add(new NonIntervalLoadError(record.MeterId, record.ServicePointId, meterInfoError));
                return null;
            }

            if(meterInfo.Count > 1)
            {
                string meterInfoError = string.Format("Multiple MeterInfo found.     Record:  {0}", record.IecCimRecord);
                Logger.Write(meterInfoError);
                Errors.Add(new NonIntervalLoadError(record.MeterId, record.ServicePointId, meterInfoError));
                return null;
            }

            return meterInfo.First();
        }

        /// <summary>
        /// Retrieves the ami quality type that matches the passed detail record
        /// </summary>
        /// <param name="record">Detail record to find the matching ami quality type for</param>
        /// <returns>AmiQualityType object if found null if not found</returns>
        public AMIQualityType GetAMIQualityType(DetailRecord record)
        {
            if (record == null)
                throw new ArgumentNullException("record");

            List<AMIQualityType> qualityType = QualityTypeCache.Where(q => q.Type == record.ReadingQuality).ToList();
            if (qualityType.Count == 0)
            {
                string qualityTypeError = string.Format("QualityType not found.     Record:  {0}", record.IecCimRecord);
                Logger.Write(qualityTypeError);
                Errors.Add(new NonIntervalLoadError(record.MeterId, record.ServicePointId, qualityTypeError));
                return null;
            }

            if (qualityType.Count > 1)
            {
                string qualityTypeError = string.Format("Multiple QualityType found.     Record:  {0}", record.IecCimRecord);
                Logger.Write(qualityTypeError);
                Errors.Add(new NonIntervalLoadError(record.MeterId, record.ServicePointId, qualityTypeError));
                return null;
            }

            return qualityType.First();
        }
        
        /// <summary>
        /// Retrieves the ami read type that matches the passed detail record
        /// </summary>
        /// <param name="record">Detail record to find the matching ami quality type for</param>
        /// <returns>AmiQualityType object if found null if not found</returns>
        public AMIReadType GetAMIReadType(DetailRecord record, Meter meter)
        {
            if (record == null)
                throw new ArgumentNullException("record");

            if (meter == null)
                throw new ArgumentNullException("meter");

            if(!meter.MeterProgramId.HasValue)
            {
                string meterProgramError = string.Format("MeterInfo does not have MeterProgram.     Record:  {0}", record.IecCimRecord);
                Logger.Write(meterProgramError);
                Errors.Add(new NonIntervalLoadError(record.MeterId, record.ServicePointId, meterProgramError));
                return null;
            }

            short meterProgramId = meter.MeterProgramId.Value;
            string readingType = record.ReadingType;
            List<AMIReadType> readTypes = ReadTypeCache.Where(r => r.Type == readingType).ToList();

            if (!AppConfigSettings.GasUnMeterProgramList.Contains(meterProgramId) && readingType == AppConfigSettings.MdmsToUnReadingType)
                readTypes = ReadTypeCache.Where(r => r.AMIReadTypeId == AppConfigSettings.GasToAmiReadType).ToList();
        
            if(AppConfigSettings.GasUnMeterProgramList.Contains(meterProgramId) && readingType == AppConfigSettings.MdmsToUnReadingType)
                readTypes = ReadTypeCache.Where(r => r.AMIReadTypeId == AppConfigSettings.GasUnAmiReadType).ToList();

            if (!AppConfigSettings.FullNetMeterProgramIdList.Contains(meterProgramId) && readingType == AppConfigSettings.MdmsToTcReadingType)
                if (LoadTypeInfo.LoadType == LoadType.SeasonChange)
                    readTypes = ReadTypeCache.Where(r => r.AMIReadTypeId == AppConfigSettings.NonNetSeasonChangeToAmiReadTypeId).ToList();
                else
                    readTypes = ReadTypeCache.Where(r => r.AMIReadTypeId == AppConfigSettings.NonNetToAmiReadTypeId).ToList();

            if (AppConfigSettings.FullNetMeterProgramIdList.Contains(meterProgramId) && readingType == AppConfigSettings.MdmsToTcReadingType)
                if(LoadTypeInfo.LoadType == LoadType.SeasonChange)
                    readTypes = ReadTypeCache.Where(r => r.AMIReadTypeId == AppConfigSettings.NetSeasonChangeTcAmiReadTYpeId).ToList();
                else
                    readTypes = ReadTypeCache.Where(r => r.AMIReadTypeId == AppConfigSettings.NetTcAmiReadTYpeId).ToList();

            if(readTypes.Count == 0)
            {
                string readTypeError = string.Format("ReadType not found.     Record:  {0}", record.IecCimRecord);
                Logger.Write(readTypeError);
                Errors.Add(new NonIntervalLoadError(record.MeterId, record.ServicePointId, readTypeError));
                return null;
            }

            if(readTypes.Count > 1)
            {
                string readTypeError = string.Format("ReadType not found.     Record:  {0}", record.IecCimRecord);
                Logger.Write(readTypeError);
                Errors.Add(new NonIntervalLoadError(record.MeterId, record.ServicePointId, readTypeError));
                return null;
            }

            return readTypes.First();
        }

        /// <summary>
        /// Load Quality and Read types into memory (based on AMISystemId = 10)
        /// </summary>
        public void CacheQualityandReadTypes()
        {
            Logger.Write("Caching quality and read types...");
            using (UocEntities context = new UocEntities())
            {
                LookupQuery query = new LookupQuery(context);
                QualityTypeCache = query.GetAmiQualityTypes();

                if (LoadTypeInfo.LoadType == LoadType.SeasonChange)
                    ReadTypeCache = query.GetSeasonChangeAmiReadTypes();
                else
                    ReadTypeCache = query.GetAmiReadTypes();
            }
            Logger.Write("Cached quality and read types.");
        }

        /// <summary>
        /// Load existing devices into memory based on Load Type from the input file name
        /// </summary>
        public void CacheExistingDevices()
        {
            Logger.Write("Caching devices...");
            using (UocEntities context = new UocEntities())
            {
               LookupQuery query = new LookupQuery(context);
               MeterInfoCache = query.GetMeterInfo(LoadTypeInfo.ServiceTypeId, LoadTypeInfo.MeterProgramIdList);
            }
            Logger.Write("Cached devices.");
        }

        /// <summary>
        /// Initializes the load type info object for current run
        /// </summary>
        public void InitializeLoadTypeInfo(LoadType currentLoadType)
        {

            Logger.Write(string.Format("Initializing load type info for {0}...", currentLoadType.ToString()));
            LoadTypeInfo.LoadType = currentLoadType;

            switch (currentLoadType)
            {
                case LoadType.CommercialElectric:
                    LoadTypeInfo.ServiceTypeId = AppConfigSettings.CommercialElectricServiceTypeID;
                    LoadTypeInfo.MeterProgramIdList = AppConfigSettings.CommercialElectricMeterProgramIDList;
                    LoadTypeInfo.FileMask = AppConfigSettings.CommercialElectricFileMask;
                    break;

                case LoadType.SeasonChange:
                    LoadTypeInfo.ServiceTypeId = AppConfigSettings.SeasonChangeServiceTypeID;
                    LoadTypeInfo.MeterProgramIdList = AppConfigSettings.SeasonChangeMeterProgramIDList;
                    LoadTypeInfo.FileMask = AppConfigSettings.SeasonChangeFileMask;
                    break;

                case LoadType.ResidentialElectric:
                    LoadTypeInfo.ServiceTypeId = AppConfigSettings.ResidentialElectricServiceTypeID;
                    LoadTypeInfo.MeterProgramIdList = AppConfigSettings.ResidentialElectricMeterProgramIDList;
                    LoadTypeInfo.FileMask = AppConfigSettings.ResidentialElectricFileMask;
                    break;

                case LoadType.NET:
                    LoadTypeInfo.ServiceTypeId = AppConfigSettings.NETServiceTypeID;
                    LoadTypeInfo.MeterProgramIdList = AppConfigSettings.NETMeterProgramIDList;
                    LoadTypeInfo.FileMask = AppConfigSettings.NETFileMask;
                    break;

                case LoadType.TOU:
                    LoadTypeInfo.ServiceTypeId = AppConfigSettings.TOUServiceTypeID;
                    LoadTypeInfo.MeterProgramIdList = AppConfigSettings.TOUMeterProgramIDList;
                    LoadTypeInfo.FileMask = AppConfigSettings.TOUFileMask;
                    break;

                case LoadType.Gas1:
                    LoadTypeInfo.ServiceTypeId = AppConfigSettings.Gas1ServiceTypeID;
                    LoadTypeInfo.MeterProgramIdList = AppConfigSettings.Gas1MeterProgramIDList;
                    LoadTypeInfo.FileMask = AppConfigSettings.Gas1FileMask;
                    break;

                case LoadType.Gas2:
                    LoadTypeInfo.ServiceTypeId = AppConfigSettings.Gas2ServiceTypeID;
                    LoadTypeInfo.MeterProgramIdList = AppConfigSettings.Gas2MeterProgramIDList;
                    LoadTypeInfo.FileMask = AppConfigSettings.Gas2FileMask;
                    break;

                default:
                    throw new Exception("Command line LoadType value is invalid.");
            }

            Logger.Write(string.Format("Initialized load type. ServiceTypeId:{0}  MeterProgramIdList:{1}  FileMask:{2}",
                        LoadTypeInfo.ServiceTypeId.ToString(),
                        String.Join(",", LoadTypeInfo.MeterProgramIdList),
                        LoadTypeInfo.FileMask));
        }
    }
}
