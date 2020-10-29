using System;

namespace Sitescope2RemoteWrite.Models
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
