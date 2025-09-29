using System.Collections.Generic;
using Sitescope2RemoteWrite.PromPb;

namespace Sitescope2RemoteWrite.Storage
{
    public interface ILabelStorage
    {
        public List<Label> GetLabels(long id);
        public bool HasLabels(long id);
        public bool QueueFull();
    }

    public class LabelDict
    {
        private Dictionary<long, List<Label>> labels = new Dictionary<long, List<Label>>(6 * 1000 * 1000);

        public bool IsEmpty()
        {
            return labels.Count == 0;
        }

        public bool Contains(long id)
        {
            return labels.ContainsKey(id);
        }

        public List<Label> GetLabels(long id)
        {
            /*if (!labels.ContainsKey(id))
                return null;*/
            if (!labels.ContainsKey(id))
                return null;
            return labels[id];
        }

        public void StoreLabels(long id, List<Label> obj)
        {
            labels[id] = obj;
            return;
        }
    }
}
