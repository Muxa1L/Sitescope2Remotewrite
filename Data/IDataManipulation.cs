using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Microsoft.Extensions.DependencyInjection
{
    public interface IDataManipulation : IDisposable
    {
        void AddXmlToQueue(XDocument xml);
    }
}
