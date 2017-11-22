using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Wec.Its.Metering.LGNonIntervalLoadCommon.Dto
{
    /// <summary>
    /// Class which represents a UOC meter
    /// </summary>
    public class Meter
    {
        /// <summary>
        /// Unique identifier for utility/meter/premise
        /// </summary>
        public int MeterInfoId { get; set; }

        /// <summary>
        /// Utilty id
        /// </summary>
        public short UtilityId { get; set; }

        /// <summary>
        /// Meter number
        /// </summary>
        public string MeterNumber { get; set; }

        /// <summary>
        /// Premise id
        /// </summary>
        public int PremiseId { get; set; }

        /// <summary>
        /// Premise service sequence
        /// </summary>
        public short PremiseServiceSequence { get; set; }

        /// <summary>
        /// Ami system id
        /// </summary>
        public short? AmiSystemId { get; set; }

        /// <summary>
        /// Meter program id
        /// </summary>
        public short? MeterProgramId { get; set; }
    }
}
