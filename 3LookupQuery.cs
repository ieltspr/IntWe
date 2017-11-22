using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data.Entity;
using Wec.Its.Metering.LGNonIntervalLoadCommon.Dto;
using Wec.Its.Metering.LGNonIntervalLoadCommon;

namespace Wec.Its.Metering.LGNonIntervalLoadDomain.DAL
{
    public class LookupQuery
    {
        /// <summary>
        /// Context to use for the meter queries
        /// </summary>
        private UocEntities _context;

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="context">Context to use for the meter queries</param>
        public LookupQuery(UocEntities context)
        {
            _context = context;
        }

        public List<AMIQualityType> GetAmiQualityTypes()
        {
            return _context.AMIQualityTypes.Where(q => q.AMISystemId == 10).ToList();
        }

        public List<AMIReadType> GetAmiReadTypes()
        {
            return _context.AMIReadTypes.Where(r => r.AMISystemId == 10 && 
                                                !r.Description.StartsWith(AppConfigSettings.SeasonChangeReadTypeFilter)).ToList();
        }

        public List<AMIReadType> GetSeasonChangeAmiReadTypes()
        {
            return _context.AMIReadTypes.Where(r => r.AMISystemId == 10 && 
                                                r.Description.StartsWith(AppConfigSettings.SeasonChangeReadTypeFilter)).ToList();
        }

        public List<Meter> GetMeterInfo(short serviceTypeId, List<short> meterProgramIdList)
        {
            if (meterProgramIdList == null)
                throw new ArgumentNullException("meterProgramIdList");

            return _context.MeterInfoes.Where(m => m.AMISystemId == 10 &&
            m.ServiceTypeId == serviceTypeId &&
            meterProgramIdList.Contains(m.MeterProgramId.Value) &&
            m.MeterInfoId1.UtilityId == 1).Select(m => new Meter()
            {
                MeterInfoId = m.MeterInfoId,
                UtilityId = m.MeterInfoId1.UtilityId,
                MeterNumber = m.MeterInfoId1.MeterNumber,
                PremiseId = m.MeterInfoId1.PremiseId,
                PremiseServiceSequence = m.MeterInfoId1.PremiseServiceSequence,
                AmiSystemId = m.AMISystemId,
                MeterProgramId = m.MeterProgramId
            }).ToList();


            //return _context.MeterInfoes.Where(m => m.AMISystemId == 10 &&
            //m.ServiceTypeId == serviceTypeId &&
            //meterProgramIdList.Contains(m.MeterProgramId.Value) &&
            //m.MeterInfoId1.UtilityId == 1).Include(m => m.MeterInfoId1).ToList();
        }

        /// <summary>
        /// Returns a count of any local (before save) or external (already saved) readings
        /// which match the passed reading.
        /// </summary>
        /// <param name="reading">Reading to search for</param>
        /// <returns>The list of readings which match the passed reading</returns>
        public List<Reading> GetReading(Reading reading)      
        {
            if (reading == null)
                throw new ArgumentNullException("reading");

            List<Reading> readings = new List<Reading>();

            //Check local entity framework cache
            readings.AddRange(_context.Readings.Local.Where(r => r.MeterInfoId == reading.MeterInfoId &&
                                r.AMIReadTypeId == reading.AMIReadTypeId &&
                                r.AMIQualityTypeId == reading.AMIQualityTypeId &&
                                r.ReadDateTime == reading.ReadDateTime).ToList());

            //Check database
            readings.AddRange(_context.Readings.Where(r => r.MeterInfoId == reading.MeterInfoId &&
                                r.AMIReadTypeId == reading.AMIReadTypeId &&
                                r.AMIQualityTypeId == reading.AMIQualityTypeId &&
                                r.ReadDateTime == reading.ReadDateTime).ToList());

            return readings;
        }
    }
}
