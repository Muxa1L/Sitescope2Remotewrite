using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MuxLibrary.Data
{
    public class Metric : ICloneable
    {
        public long MetricId;
        public DateTime time;
        public double? value_double;
        public String value_string;

        public object Clone()
        {
            return this.MemberwiseClone();
        }
    }
}
