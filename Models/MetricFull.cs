using System;
using System.Collections.Generic;

namespace Sitescope2RemoteWrite.Models
{
    public class MetricFull : ICloneable
    {
        public string Path;
        public string Name;
        public string Type;
        public string Target;
        public string TargetIP;
        public string SourceTemplateName;
        public string TMID;
        public string MetricName;
        public string MetricUnit;
        public string MonitoringModule;
        public string MR;
        public string SystemName;
        public string ServerTM;
        public string TemplateName;
        public string Instance;
        public long Timestamp;
        public string value;

        public object Clone()
        {
            return this.MemberwiseClone();
        }

        public Monitor ToMonitor()
        {
            var result = new Monitor();
            result.name = this.Name;
            result.path = this.Path;
            result.sourceTemplateName = this.SourceTemplateName;
            result.target = this.Target;
            result.targetIP = this.TargetIP;
            result.type = this.Type;
            result.timestamp = this.Timestamp;
            result.Counters = new List<Counter>{
                new Counter{
                    name = this.MetricName,
                    value = this.value
                }
            };
            return result;
        }
    }
}
