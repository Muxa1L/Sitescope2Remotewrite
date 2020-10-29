using Sitescope2RemoteWrite.PromPb;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Sitescope2RemoteWrite.Queueing
{
    public interface IMonitorQueue
    {
        void EnqueueMonitor(Models.Monitor monitor);

        Task<Models.Monitor> DequeueAsync(
            CancellationToken cancellationToken);
    }

    public class MonitorQueue : IMonitorQueue
    {
        private ConcurrentQueue<Models.Monitor> _workItems =
            new ConcurrentQueue<Models.Monitor>();
        private SemaphoreSlim _signal = new SemaphoreSlim(0);

        public async Task<Models.Monitor> DequeueAsync(CancellationToken cancellationToken)
        {
            await _signal.WaitAsync(cancellationToken);
            _workItems.TryDequeue(out var workItem);
            return workItem;
        }


        void IMonitorQueue.EnqueueMonitor(Models.Monitor workItem)
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