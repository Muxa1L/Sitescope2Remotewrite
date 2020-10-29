using System.Collections.Generic;

namespace Sitescope2RemoteWrite.Models
{
    public class Monitor//:IDisposable
    {
        public string path;
        public string type;
        public string target;
        public string targetIP;
        public long timestamp;
        public string sourceTemplateName;
        public string name;
        public List<Counter> Counters;
    }
    public class Counter// : IDisposable
    {
        public string value;
        public string name;
    }
}
