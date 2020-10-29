using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Sitescope2RemoteWrite.Queueing
{
    public interface IXmlTaskQueue
    {
        void EnqueueXml(XDocument xml);

        Task<XDocument> DequeueAsync(
            CancellationToken cancellationToken);
    }

    public class XmlTaskQueue : IXmlTaskQueue
    {
        private ConcurrentQueue<XDocument> _workItems =
            new ConcurrentQueue<XDocument>();
        private BlockingCollection<XDocument> _workItems2 =
            new BlockingCollection<XDocument>(new ConcurrentQueue<XDocument>());
        private SemaphoreSlim _signal = new SemaphoreSlim(0);

        public async Task<XDocument> DequeueAsync(CancellationToken cancellationToken)
        {
            await _signal.WaitAsync(cancellationToken);
            _workItems.TryDequeue(out var workItem);
            return workItem;
        }


        void IXmlTaskQueue.EnqueueXml(XDocument workItem)
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