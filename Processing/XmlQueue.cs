using System;
using System.Threading;
using System.Threading.Tasks;

namespace Sitescope2RemoteWrite.Processing{
    interface IXmlTaskQueue{
        void QueueXml (Func<CancellationToken, Task> xml);

        Task<Func<CancellationToken, Task>> DequeueAsync(
            CancellationToken cancellationToken);
    }

    public class XmlTaskQueue : IXmlTaskQueue
    {
        Task<Func<CancellationToken, Task>> IXmlTaskQueue.DequeueAsync(CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        void IXmlTaskQueue.QueueXml(Func<CancellationToken, Task> xml)
        {
            throw new NotImplementedException();
        }
    }
}