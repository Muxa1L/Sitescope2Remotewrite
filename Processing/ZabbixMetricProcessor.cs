using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using Newtonsoft.Json;
using Sitescope2RemoteWrite.Helpers;
using Sitescope2RemoteWrite.Models;
using Sitescope2RemoteWrite.PromPb;
using Sitescope2RemoteWrite.Queueing;
using Sitescope2RemoteWrite.Storage;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
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

    

    public class ZabbixMetricProcessor : BackgroundService
    {
        private readonly ILogger<ZabbixMetricProcessor> _logger;
        private readonly IConfiguration zbxConfig;
        private readonly ITimeSeriesQueue tsQueue;
        private readonly IZabbixMetricQueue zbxQueue;
        private readonly ILabelStorage _labelStorage;
        
        private readonly IFormatProvider culture = new System.Globalization.CultureInfo("en-US");

        public ZabbixMetricProcessor(IConfiguration config, ILogger<ZabbixMetricProcessor> logger, ILabelStorage labelStorage, IZabbixMetricQueue zabbixMetricQueue, ITimeSeriesQueue timeSeriesQueue)
        {
            _logger = logger;
            zbxConfig = config.GetSection("zabbix");
            _labelStorage = labelStorage;
            zbxQueue = zabbixMetricQueue;
            tsQueue = timeSeriesQueue;
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
                var queueFull = _labelStorage.QueueFull();
                if (queueFull)
                    _logger.LogInformation("Locking zabbix metric processing, because of not known queue is full");
                SpinWait.SpinUntil(() => { return !_labelStorage.QueueFull() || stoppingToken.IsCancellationRequested; });
                if (queueFull)
                    _logger.LogInformation("Unlocking zabbix metric processing");
                var monitor = await zbxQueue.DequeueAsync(stoppingToken);
                try
                {
                    ProcessMetrics(monitor);
                }

                catch (Exception ex)
                {
                    _logger.LogCritical("Could not process monitor \r\n{0}\r\n{1}", ex.Message, ex.StackTrace);

                }
            }
        }

        private void ProcessMetrics(Models.ZabbixMetric metric)
        {
            //var labels = _labelStorage.GetLabels(metric.itemId);
            if (_labelStorage.HasLabels(metric.itemId))
            {
                var ts = new TimeSeries();
                ts.SetLabels(_labelStorage.GetLabels(metric.itemId));
                ts.AddSample(metric.time, metric.value);
                tsQueue.Enqueue(ts);
            }
            else
            {
                zbxQueue.EnqueueForce(metric);
            }

        }

        



        //itm.itemid, itm.templateid, itm.interfaceid, hst.proxy_hostid, itm.state, itm.type item_type, itm.description item_descr,
        

        
    }
}