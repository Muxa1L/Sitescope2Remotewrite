using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Sitescope2RemoteWrite.Helpers;
using Sitescope2RemoteWrite.PromPb;
using Sitescope2RemoteWrite.Queueing;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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

    public class MonitorProcessor : BackgroundService
    {
        private readonly ILogger<XmlProcessor> _logger;
        private readonly IConfiguration processingConfig;
        public IServiceProvider Services { get; }
        private readonly RegexProcess RegexProcess;
        public MonitorProcessor(IServiceProvider services, ILogger<XmlProcessor> logger)
        {
            _logger = logger;
            Services = services;
            using (var scope = Services.CreateScope())
            {
                //PathRegexps = new List<Regex>();
                var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
                processingConfig = config.GetSection("ProcessingConfig");
                RegexProcess = new RegexProcess(processingConfig);
                
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {

            _logger.LogInformation(
                "Monitor Processor started"
            );
            await DoWork(stoppingToken);
        }

        private async Task DoWork(CancellationToken stoppingToken)
        {
            IMonitorQueue monitorQueue;
            ITimeSeriesQueue timeSeriesQueue;
            using (var scope = Services.CreateScope())
            {
                monitorQueue = scope.ServiceProvider.GetRequiredService<IMonitorQueue>();
                timeSeriesQueue = scope.ServiceProvider.GetRequiredService<ITimeSeriesQueue>();
            }
            while (!stoppingToken.IsCancellationRequested)
            {
                var monitor = await monitorQueue.DequeueAsync(stoppingToken);
                try
                {
                    ProcessMonitors(monitor, timeSeriesQueue);
                }

                catch (Exception ex)
                {
                    _logger.LogCritical("Could not process monitor \r\n{0}\r\n{1}", ex.Message, ex.StackTrace);

                }
            }
        }

        private void ProcessMonitors(Models.Monitor monitor, ITimeSeriesQueue timeSeriesQueue)
        {
            var baseTS = new TimeSeries();
            baseTS.AddLabel("path", monitor.path);
            baseTS.AddLabel("name", monitor.name);
            baseTS.AddLabel("type", monitor.type);
            baseTS.AddLabel("target", monitor.target.ToLower());
            baseTS.AddLabel("targetip", monitor.targetIP.ToLower());
            RegexProcess.AddLabelsFromPath(monitor.path, ref baseTS);

            var timeSeries = RegexProcess.ProcessCounters(baseTS, monitor);
            /*foreach (var pathRex in PathRegexps)
            {
                var match = pathRex.Match(monitor.path);
                if (match.Success)
                {
                    for (int i = 1; i < match.Groups.Count; i++) //(Group group in match.Groups)
                    {
                        Group group = match.Groups[i];
                        if (!String.IsNullOrEmpty(group.Name))
                        {
                            var val = group.Value;
                            if (String.IsNullOrEmpty(val))
                                val = "empty";
                            timeSeries.AddLabel(group.Name, val);
                        }
                    }
                    break;
                }
            }*/
            //timeSeries.AddLabel("sourcetemplatename", monitor.sourceTemplateName);
            //timeSeries.Ad
            ///throw new NotImplementedException();
        }
    }
}