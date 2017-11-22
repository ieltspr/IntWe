using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Wec.Its.Metering.LGNonIntervalLoadDomain.Manager;
using System.Configuration;

namespace Wec.Its.Metering.LGNonIntervalLoad
{
    public class Program
    {
        public static Int32 Main()
        {
            try
            {
                ApplicationManager manager = new ApplicationManager();
                manager.RunApplication();
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
                return 8000;
            }
            return 0;
        }
    }
}
