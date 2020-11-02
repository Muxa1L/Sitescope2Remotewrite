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

        void EnqueueList(List<TimeSeries> tsList);

        Task<TimeSeries> DequeueAsync(
            CancellationToken cancellationToken);

        TimeSeries Dequeue();
    }

    public class TimeSeriesQueue : ITimeSeriesQueue
    {
        private readonly ConcurrentQueue<TimeSeries> _workItems =
            new ConcurrentQueue<TimeSeries>();
        private readonly SemaphoreSlim _signal = new SemaphoreSlim(0);

        public async Task<TimeSeries> DequeueAsync(CancellationToken cancellationToken)
        {
            await _signal.WaitAsync(cancellationToken);
            _workItems.TryDequeue(out var workItem);
            return workItem;
        }

        public TimeSeries Dequeue()
        {
            _workItems.TryDequeue(out var workItem);
            return workItem;
        }

        public void EnqueueList(List<TimeSeries> tsList)
        {
            foreach (var timeSerie in tsList)
            {
                this.Enqueue(timeSerie);
            }
        }

        public void Enqueue(TimeSeries workItem)
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