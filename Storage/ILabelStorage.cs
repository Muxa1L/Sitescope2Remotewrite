using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
        private Dictionary<long, List<Label>> labels = new Dictionary<long, List<Label>>(10 * 1000 * 1000);
        private Dictionary<long, DateTime> lastAccess = new Dictionary<long, DateTime>(10 * 1000 * 1000);
        Timer cleaner;

        public LabelDict()
        {
            //cleaner = new Timer(Cleanup, null, 3600 * 1000, 3600 * 1000);
        }

        public void Cleanup(object state)
        {
            var notAccessed = lastAccess.Where(x => x.Value < DateTime.Now.AddHours(-1)).Select(x => x.Key).ToList();
            foreach (var toRemove in notAccessed)
            {
                labels.Remove(toRemove);
                lastAccess.Remove(toRemove);
            }
            Console.WriteLine($"removed {notAccessed.Count} not accessed for 1h labels");
        }

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
            lastAccess[id] = DateTime.Now;
            return labels[id];
        }

        public void StoreLabels(long id, List<Label> obj)
        {
            labels[id] = obj;
            lastAccess[id] = DateTime.MinValue;
            return;
        }
    }
}
