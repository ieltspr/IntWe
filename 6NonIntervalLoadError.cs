using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Wec.Its.Metering.LGNonIntervalLoadCommon.Dto
{
    public class NonIntervalLoadError
    {
        /// <summary>
        /// Default constructor
        /// </summary>
        public NonIntervalLoadError()
        {
            MeterNumber = string.Empty;
            PremiseId = string.Empty;
            ErrorMessage = string.Empty;
        }

        public NonIntervalLoadError(string meterNumber, string premiseId, string errorMessage)
        {
            MeterNumber = meterNumber;
            PremiseId = premiseId;
            ErrorMessage = errorMessage;
        }

        public string MeterNumber { get; set; }

        public string PremiseId { get; set; }

        public string ErrorMessage { get; set; }
    }
}
