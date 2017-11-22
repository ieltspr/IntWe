using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Wec.Its.Metering.LGNonIntervalLoadCommon.Enum;

namespace Wec.Its.Metering.LGNonIntervalLoadCommon.Dto
{
    /// <summary>
    /// Holds the current runs load type information
    /// </summary>
    public class LoadTypeInfo
    {
        /// <summary>
        /// Default Constructor
        /// </summary>
        public LoadTypeInfo()
        {
            ServiceTypeId = 0;
            MeterProgramIdList = new List<short>();
            FileMask = string.Empty;
            LoadType = LoadType.Default;
        }

        /// <summary>
        /// Service type for load
        /// </summary>
        public static short ServiceTypeId { get; set; }
        
        /// <summary>
        /// Meter program id list for load
        /// </summary>
        public static List<short> MeterProgramIdList { get; set; }

        /// <summary>
        /// File mask for load
        /// </summary>
        public static string FileMask { get; set; }

        /// <summary>
        /// Load type for load
        /// </summary>
        public static LoadType LoadType { get; set; }
    }
}
