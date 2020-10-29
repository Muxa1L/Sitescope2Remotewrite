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
        private IConfiguration processingConfig;
        public IServiceProvider Services { get; }
        private List<Regex> PathRegexps;
        public MonitorProcessor(IServiceProvider services, ILogger<XmlProcessor> logger)
        {
            _logger = logger;
            Services = services;
            using (var scope = Services.CreateScope())
            {
                PathRegexps = new List<Regex>();
                var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
                processingConfig = config.GetSection("ProcessingConfig");
                foreach (var pathRex in processingConfig.GetSection("PathsProcessing").Get<List<Dictionary<string, string>>>())
                {
                    var rex = new Regex(pathRex.First().Value, RegexOptions.Compiled);
                    PathRegexps.Add(rex);
                }
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
            var timeSeries = new TimeSeries();
            timeSeries.AddLabel("path", monitor.path);
            timeSeries.AddLabel("name", monitor.name);
            timeSeries.AddLabel("type", monitor.type);
            timeSeries.AddLabel("target", monitor.target.ToLower());
            timeSeries.AddLabel("targetip", monitor.targetIP.ToLower());
            foreach (var pathRex in PathRegexps)
            {
                var match = pathRex.Match(monitor.path);
                if (match.Success)
                {
                    foreach (Group group in match.Groups)
                    {
                        if (group.Index != 0) // at zero index full match is stored. We dont need it.
                        {
                            if (!String.IsNullOrEmpty(group.Name))
                            {
                                var val = group.Value;
                                if (String.IsNullOrEmpty(val))
                                    val = "empty";
                                timeSeries.AddLabel(group.Name, val);
                            }
                            
                        }
                        //timeSeries.AddLabel(group.n)
                    }
                    break;
                }
            }
            //timeSeries.AddLabel("sourcetemplatename", monitor.sourceTemplateName);
            //timeSeries.Ad
            ///throw new NotImplementedException();
        }
    }
}