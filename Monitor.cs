using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MuxLibrary.Data
{
    public class Monitor//:IDisposable
    {
        public string path;
        public string type;
        public string target;
        public string targetIP;
        public DateTime time;
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
