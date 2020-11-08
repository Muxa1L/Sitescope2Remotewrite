using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Sitescope2RemoteWrite.Queueing
{
    public interface IDebugQueue
    {
        void AddPath(string path);

        void AddMetric(string metricName);

        string[] GetPaths();

        string[] GetMetrics();
    }

    public class DebugQueue : IDebugQueue
    {
        private ConcurrentDictionary<string, int> _paths =
            new ConcurrentDictionary<string, int>();

        private ConcurrentDictionary<string, int> _metrics =
            new ConcurrentDictionary<string, int>();

        public void AddMetric(string metricName)
        {
            if (!_metrics.ContainsKey(metricName))
            {
                _metrics.TryAdd(metricName, 0);
            }
        }

        public void AddPath(string path)
        {
            if (!_paths.ContainsKey(path))
            {
                _paths.TryAdd(path, 0);
            }
            
        }

        public string[] GetMetrics()
        {
            return _metrics.Select(x => x.Key).ToArray();
        }

        public string[] GetPaths()
        {
            return _paths.Select(x => x.Key).ToArray();
        }
    }
}