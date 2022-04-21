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
        private readonly ILogger<MonitorProcessor> _logger;
        private readonly IConfiguration _processingConfig;
        private readonly IMonitorQueue _monitorQueue;
        private readonly ITimeSeriesQueue _timeSeriesQueue;
        private readonly RegexProcess _regexProcess;
        private readonly IDebugQueue debugQueue;

        public MonitorProcessor(IConfiguration config, ILogger<MonitorProcessor> logger, IDebugQueue debugQueue, ITimeSeriesQueue timeSeriesQueue, IMonitorQueue monitorQueue)
        {
            _logger = logger;
            _monitorQueue = monitorQueue;
            _timeSeriesQueue = timeSeriesQueue;
            //Services = services;
            this.debugQueue = debugQueue;
            _processingConfig = config.GetSection("Processing");
            _regexProcess = new RegexProcess(_processingConfig);
            
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
            while (!stoppingToken.IsCancellationRequested)
            {
                var monitor = await _monitorQueue.DequeueAsync(stoppingToken);
                try
                {
                    ProcessMonitors(monitor, _timeSeriesQueue);
                }

                catch (Exception ex)
                {
                    _logger.LogCritical("Could not process monitor \r\n{0}\r\n{1}", ex.Message, ex.StackTrace);

                }
            }
        }

        public void ProcessMonitors(Models.Monitor monitor, ITimeSeriesQueue timeSeriesQueue)
        {
            var baseTS = new TimeSeries();
            baseTS.AddLabel("path", monitor.path);
            baseTS.AddLabel("name", monitor.name);
            baseTS.AddLabel("type", monitor.type);
            baseTS.AddLabel("target", monitor.target.ToLower());
            baseTS.AddLabel("targetip", monitor.targetIP.ToLower());
            var pathFound = _regexProcess.AddLabelsFromPath(monitor.path, ref baseTS);
            if (!pathFound)
            {
                debugQueue.AddPath(monitor.path);
            }
            //monitor.timestamp = DateTime.UtcNow.ToUnixTimeStamp() * 1000;
            var timeSeries = _regexProcess.ProcessCounters(baseTS, monitor);
            timeSeriesQueue.EnqueueList(timeSeries);
        }
    }
}