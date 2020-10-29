using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sitescope2RemoteWrite.Helpers;
using Sitescope2RemoteWrite.Queueing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Sitescope2RemoteWrite.Processing
{
    /*internal interface IXmlProcessor
    {
        Task DoWork(CancellationToken stoppingToken);
    }

    internal class XmlProcessor : IXmlProcessor
    {
        public Task DoWork(CancellationToken stoppingToken)
        {
            throw new NotImplementedException();
        }
    }*/

    public class XmlProcessor : BackgroundService
    {
        private readonly ILogger<XmlProcessor> _logger;
        private readonly ConcurrentQueue<XDocument> _queue;
        public IServiceProvider Services { get; }
        public XmlProcessor(IServiceProvider services, ILogger<XmlProcessor> logger)
        {
            _queue = new ConcurrentQueue<XDocument>();
            _logger = logger;
            Services = services;
        }

        public void EnqueueTask(XDocument task)
        {
            if (task != null)
            {
                _queue.Enqueue(task);
            }
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {

            _logger.LogInformation(
                "Xml Processor started"
            );
            await DoWork(stoppingToken);
        }
        private async Task DoWork(CancellationToken stoppingToken)
        {
            IXmlTaskQueue queue;
            using (var scope = Services.CreateScope())
            {
                queue = scope.ServiceProvider.GetRequiredService<IXmlTaskQueue>();
            }
            while (!stoppingToken.IsCancellationRequested)
            {
                var xml = await queue.DequeueAsync(stoppingToken);
                try
                {
                    var monitors = GetMonitors(xml);
                }

                catch (Exception ex)
                {
                    _logger.LogCritical("Could not get data from doc {0}\r\n\r\n{1}", ex.Message, xml.ToString());

                }
            }
        }

        private List<Models.Monitor> GetMonitors(XDocument xml)
        {
            var source = xml.Element("performanceMonitors").Attribute("collectorHost").Value.ToLower();
            var monitors = xml.Descendants("monitor");
            var result = new List<Models.Monitor>(monitors.Count());
            foreach (var monitor in monitors)
            {
                var mon = new Models.Monitor()
                {
                    path = source + monitor.Path(),
                    type = monitor.Attribute("type").Value,
                    name = monitor.Attribute("name").Value,
                    target = monitor.Attribute("target").Value,
                    targetIP = monitor.Attribute("targetIP").Value,
                    timestamp = long.Parse(monitor.Attribute("time").Value),
                    sourceTemplateName = monitor.Attribute("sourceTemplateName") == null ? "" : monitor.Attribute("sourceTemplateName").Value
                };

                var counters = new List<Models.Counter>();
                foreach (var metr in monitor.Elements())
                {
                    var cntr = new Models.Counter
                    {
                        name = (string)metr.Attribute("name"),
                        value = (string)metr.Attribute("value")
                    };
                    counters.Add(cntr);
                }
                mon.Counters = counters;
                result.Add(mon);
            }
            return result;
        }
    }
}