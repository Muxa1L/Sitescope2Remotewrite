using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using System.Threading.Tasks;

namespace Sitescope2RemoteWrite.Storage
{
    public interface ILabelStorage
    {
        public void GetLabels(long id);
        public bool LabelsKnown(long id);
    }
    public class LabelStorage: ILabelStorage
    {
        private Dictionary<long, List<Label>> labels = new Dictionary<long, List<Label>>();

        private List<long> notKnown = new List<long>();

        public LabelStorage()
        {

        }

        public void GetLabels(long id)
        {

            throw new NotImplementedException();
        }

        public bool LabelsKnown(long id)
        {
            throw new NotImplementedException();
        }

        /*public class LabelDict
        {
            private Dictionary<long, byte[]> labels = new Dictionary<long, byte[]>();
            private Dictionary<long, List<Label>> labels_Uncompressed = new Dictionary<long, List<Label>>();

            public List<Label> GetLabels(long id)
            {
                if (!labels.ContainsKey(id))
                    return null;
                return labels_Uncompressed[id];
                using (MemoryStream ms = new MemoryStream(labels[id]))
                {
                    using (GZipStream zs = new GZipStream(ms, CompressionMode.Decompress, true))
                    {
                        BinaryFormatter bf = new BinaryFormatter();
                        return (List<Label>)bf.Deserialize(zs);
                    }
                }
            }

            public void StoreLabels(long id, List<Label> obj)
            {
                labels_Uncompressed[id] = obj;
                return;
                using (MemoryStream ms = new MemoryStream())
                {
                    using (GZipStream zs = new GZipStream(ms, CompressionMode.Compress, true))
                    {
                        BinaryFormatter bf = new BinaryFormatter();
                        bf.Serialize(zs, obj);

                    }
                    labels[id] = ms.ToArray();
                }
            }
        }*/

        
    }
}
