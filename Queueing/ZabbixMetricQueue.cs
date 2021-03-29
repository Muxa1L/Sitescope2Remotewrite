using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sitescope2RemoteWrite.PromPb;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Sitescope2RemoteWrite.Queueing
{
    public interface IZabbixMetricQueue
    {
        void Enqueue(Models.ZabbixMetric metric);

        void EnqueueForce(Models.ZabbixMetric metric);

        Task<Models.ZabbixMetric> DequeueAsync(
            CancellationToken cancellationToken);

        bool IsFull();
    }

    public class ZabbixMetricQueue : IZabbixMetricQueue
    {
        private readonly ConcurrentQueue<Models.ZabbixMetric> _workItems =
            new ConcurrentQueue<Models.ZabbixMetric>();
        private readonly SemaphoreSlim _signal = new SemaphoreSlim(0);

        private readonly int maxQueueSize;

        private ILogger<ZabbixMetricQueue> _logger;

        public ZabbixMetricQueue(IConfiguration config, ILogger<ZabbixMetricQueue> logger)
        {
            _logger = logger;
            maxQueueSize = config.GetValue<int>("zabbix:maxQueueSize", 20000);
        }

        public async Task<Models.ZabbixMetric> DequeueAsync(CancellationToken cancellationToken)
        {
            await _signal.WaitAsync(cancellationToken);
            _workItems.TryDequeue(out var workItem);
            return workItem;
        }

        public bool IsFull()
        {
            return _workItems.Count >= maxQueueSize;
        }

        void IZabbixMetricQueue.Enqueue(Models.ZabbixMetric workItem)
        {
            if (workItem == null)
            {
                throw new ArgumentNullException(nameof(workItem));
            }
            var wasLocked = false;
            if (_workItems.Count >= maxQueueSize)
            {
                _logger.LogInformation($"Waiting for queue cleanup. Currently {_workItems.Count} messages");
                wasLocked = true;
            }

            SpinWait.SpinUntil(() => { return _workItems.Count < maxQueueSize; });
            if (wasLocked)
                _logger.LogInformation($"Queue cleaned up. Currently {_workItems.Count} messages");
            _workItems.Enqueue(workItem);
            _signal.Release();
        }

        void IZabbixMetricQueue.EnqueueForce(Models.ZabbixMetric workItem)
        {
            if (workItem == null)
            {
                throw new ArgumentNullException(nameof(workItem));
            }
            _workItems.Enqueue(workItem);
            _signal.Release();
        }
    }
}