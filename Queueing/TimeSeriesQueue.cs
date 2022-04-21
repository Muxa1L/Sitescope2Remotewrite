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

    public interface ITimeSeriesQueue
    {
        void Enqueue(TimeSeries timeSeries);

        void EnqueueForce(TimeSeries timeSeries);

        void EnqueueList(List<TimeSeries> tsList);

        Task<TimeSeries> DequeueAsync(
            CancellationToken cancellationToken);

        int Length();

        //TimeSeries Dequeue();
    }

    public class TimeSeriesQueue : ITimeSeriesQueue
    {
        private readonly ConcurrentQueue<TimeSeries> _workItems =
            new ConcurrentQueue<TimeSeries>();
        private readonly SemaphoreSlim _signal = new SemaphoreSlim(0);

        ILogger<TimeSeriesQueue> _logger;

        public TimeSeriesQueue(ILogger<TimeSeriesQueue> logger)
        {
            _logger = logger;
        }

        public async Task<TimeSeries> DequeueAsync(CancellationToken cancellationToken)
        {
            await _signal.WaitAsync(cancellationToken);
            _workItems.TryDequeue(out var workItem);
            return workItem;
        }

        /*public TimeSeries Dequeue()
        {
            _workItems.TryDequeue(out var workItem);
            return workItem;
        }*/

        public void EnqueueList(List<TimeSeries> tsList)
        {
            
            foreach (var timeSerie in tsList)
            {
                this.Enqueue(timeSerie);
            }
        }

        public void Enqueue(TimeSeries workItem)
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

        public void EnqueueForce(TimeSeries workItem)
        {
            if (workItem == null)
            {
                throw new ArgumentNullException(nameof(workItem));
            }
            _workItems.Enqueue(workItem);
            _signal.Release();
        }

        public int Length()
        {
            return _workItems.Count;
        }
    }
}