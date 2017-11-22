using System;
using System.IO;
using System.Data;
using System.Linq;
using System.Text;
using OfficeOpenXml;
using System.Net.Mail;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Teg.Its.Metering.Core;
using Wec.Its.Metering.LGNonIntervalLoadCommon;
using Wec.Its.Metering.LGNonIntervalLoadCommon.Dto;
using System.Collections;

namespace Wec.Its.Metering.LGNonIntervalLoadDomain.Manager
{
    public class ErrorReportManager
    {
        public ErrorReportManager() { }

        public void CreateReport(ConcurrentBag<NonIntervalLoadError> errors, decimal nbrExpectedDvcs, decimal nbrProcessedDvcs)
        {
            if (errors == null)
            {
                throw new ArgumentNullException("errors");
            }

            if (nbrExpectedDvcs == 0)
            {
                Logger.Write("Expected device count of 0 passed to Report.  Cannot calculate device %.");
                return;
            }

            if (nbrProcessedDvcs == 0)
            {
                Logger.Write("Processed device count of 0 passed to Report.  Will calculate device % of 0.");
            }

            if (nbrExpectedDvcs != nbrProcessedDvcs)
                Logger.Write(string.Format("Report counts not equal.  Expected {0} vs Processed {1}.",
                    nbrExpectedDvcs.ToString(), nbrProcessedDvcs.ToString()));

            decimal percentDevices;
            percentDevices = nbrProcessedDvcs / nbrExpectedDvcs;

            string calcThreshold = "";
            if (percentDevices < AppConfigSettings.ReadingThreshold)
            {
                calcThreshold = "LOW";
            }
            else
            {
                calcThreshold = "ACCEPTABLE";
            }

            string rptLine1 = "LGNonIntervalLoad.exe encountered some discrepancies/errors when processing today's " +
                 LoadTypeInfo.LoadType + " file(s).";
            string rptLine3 = "% of meters received in the L&G file(s) is:  " +  calcThreshold;

            string rptLine4 = "Expected = "
                + nbrExpectedDvcs.ToString()
                + " devices VS Processed = "
                + nbrProcessedDvcs.ToString()
                + " devices.  Actual received = "
                + (Math.Round((percentDevices * 100), 2).ToString())
                + "% VS desired Threshold of "
                + AppConfigSettings.ReadingThreshold.ToString()
                + "%.";

            string rptLine6 = "The following meters have errors:  ";
            string rptBlankLine = " ";
            string rptColHdg1 = "Meter";
            string rptColHdg2 = "Premise";
            string rptColHdg3 = "Error Details";

            string fileName = @"\LGNonIntervalLoadErrRpt-" + LoadTypeInfo.LoadType + 
                "-" + DateTime.Now.ToString("MMddyyyyhhmmss") + ".xlsx";
            FileInfo newFile = new FileInfo(AppConfigSettings.ErrorReportFilePathRoot + fileName);

            using (ExcelPackage errorReport = new ExcelPackage(newFile))
            {
                errorReport.Workbook.Worksheets.Add("LGNonIntervalLoadErrors");
                ExcelWorksheet intervalLoadErrors = errorReport.Workbook.Worksheets[1];
                intervalLoadErrors.Name = "LGNonIntervalLoadErrors" + "-" + LoadTypeInfo.LoadType;

                int rowIndex = 1;
                intervalLoadErrors.Column(1).Width = 20;
                intervalLoadErrors.Column(2).Width = 20;

                //upfront lines at top
                intervalLoadErrors.Cells[rowIndex, 1].Value = rptLine1;
                rowIndex++;
                intervalLoadErrors.Cells[rowIndex, 1].Value = rptBlankLine;
                rowIndex++;
                intervalLoadErrors.Cells[rowIndex, 1].Value = rptLine3;
                rowIndex++;
                intervalLoadErrors.Cells[rowIndex, 1].Value = rptLine4;
                rowIndex++;
                intervalLoadErrors.Cells[rowIndex, 1].Value = rptBlankLine;
                rowIndex++;
                intervalLoadErrors.Cells[rowIndex, 1].Value = rptLine6;
                rowIndex++;

                //column headings
                intervalLoadErrors.Cells[rowIndex, 1].Value = rptColHdg1;
                intervalLoadErrors.Cells[rowIndex, 1].Style.Font.Bold = true;
                intervalLoadErrors.Cells[rowIndex, 2].Value = rptColHdg2;
                intervalLoadErrors.Cells[rowIndex, 2].Style.Font.Bold = true;
                intervalLoadErrors.Cells[rowIndex, 3].Value = rptColHdg3;
                intervalLoadErrors.Cells[rowIndex, 3].Style.Font.Bold = true;
                rowIndex++;

                //write out list of cached errors
                foreach (NonIntervalLoadError Error in errors)
                {
                    intervalLoadErrors.Cells[rowIndex, 1].Value = Error.MeterNumber;
                    intervalLoadErrors.Cells[rowIndex, 2].Value = Error.PremiseId;
                    intervalLoadErrors.Cells[rowIndex, 3].Value = Error.ErrorMessage;
                    rowIndex++;
                }

                //save the excel
                errorReport.Save();
            }

            if (!CommandLineOptions.EmailReport)
            {
                Logger.Write("Email command line option set to False - no email generated.");
                return;
            }

            if (errors.Count == 0)
            {
                Logger.Write("Error list passed to Report is empty - no email generated.");
                return;
            }

            //email the excel
            List<string> emailList = new List<string>();

                if (!string.IsNullOrEmpty(AppConfigSettings.ErrorReportEmailTo))
                {
                    emailList.AddRange(AppConfigSettings.ErrorReportEmailTo.Split(',').ToList());
                }

                if (emailList.Count > 0)
                {
                    MailMessage newMailMessage = new MailMessage();

                    foreach (string email in emailList)
                    {
                        newMailMessage.To.Add(email);
                    }

                    newMailMessage.From = new MailAddress(AppConfigSettings.MailFromAddress);
                    newMailMessage.Subject = AppConfigSettings.ErrorReportEmailSubject + "-" + LoadTypeInfo.LoadType;
                    newMailMessage.Body = AppConfigSettings.ErrorReportEmailBody + System.Environment.NewLine + AppConfigSettings.ErrorReportFilePathRoot + fileName;
                    newMailMessage.IsBodyHtml = true;

                    Attachment reportAttachment = new Attachment(AppConfigSettings.ErrorReportFilePathRoot + fileName);
                    newMailMessage.Attachments.Add(reportAttachment);

                    SmtpClient smtp = new SmtpClient(AppConfigSettings.MailRelayServer);
                    smtp.Send(newMailMessage);
                }
        }        
    }
}
