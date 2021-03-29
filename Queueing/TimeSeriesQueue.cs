using Microsoft.Extensions.Logging;
using Sitescope2RemoteWrite.PromPb;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Sitescope2RemoteWrite.Queueing
{
    public class ShortTimeserie
    {
        public long id;
        public long time;
        public double value;
    }

    public interface ITimeSeriesQueue
    {
        void Enqueue(ShortTimeserie timeSeries);

        void EnqueueForce(ShortTimeserie timeSeries);

        void EnqueueList(List<ShortTimeserie> tsList);

        Task<ShortTimeserie> DequeueAsync(
            CancellationToken cancellationToken);

        //ShortTimeserie Dequeue();
    }

    public class TimeSeriesQueue : ITimeSeriesQueue
    {
        private readonly ConcurrentQueue<ShortTimeserie> _workItems =
            new ConcurrentQueue<ShortTimeserie>();
        private readonly SemaphoreSlim _signal = new SemaphoreSlim(0);

        ILogger<TimeSeriesQueue> _logger;

        public TimeSeriesQueue(ILogger<TimeSeriesQueue> logger)
        {
            _logger = logger;
        }

        public async Task<ShortTimeserie> DequeueAsync(CancellationToken cancellationToken)
        {
            await _signal.WaitAsync(cancellationToken);
            _workItems.TryDequeue(out var workItem);
            return workItem;
        }

        /*public ShortTimeserie Dequeue()
        {
            _workItems.TryDequeue(out var workItem);
            return workItem;
        }*/

        public void EnqueueList(List<ShortTimeserie> tsList)
        {
            
            foreach (var timeSerie in tsList)
            {
                this.Enqueue(timeSerie);
            }
        }

        public void Enqueue(ShortTimeserie workItem)
        {
            var wasLocked = false;
            if (_workItems.Count >= 400000)
            {
                _logger.LogInformation($"Waiting for queue cleanup. Currently {_workItems.Count} messages");
                wasLocked = true;
            }
                
            SpinWait.SpinUntil(() => { return _workItems.Count < 400000; });
            if (wasLocked)
                _logger.LogInformation($"Queue cleaned up. Currently {_workItems.Count} messages");
            if (workItem == null)
            {
                throw new ArgumentNullException(nameof(workItem));
            }
            _workItems.Enqueue(workItem);
            _signal.Release();
        }

        public void EnqueueForce(ShortTimeserie workItem)
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