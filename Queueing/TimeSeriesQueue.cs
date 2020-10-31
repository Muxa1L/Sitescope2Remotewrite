using Sitescope2RemoteWrite.PromPb;
using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Sitescope2RemoteWrite.Queueing
{
    public interface ITimeSeriesQueue
    {
        void EnqueueXml(TimeSeries timeSeries);

        Task<TimeSeries> DequeueAsync(
            CancellationToken cancellationToken);
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


        void ITimeSeriesQueue.EnqueueXml(TimeSeries workItem)
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