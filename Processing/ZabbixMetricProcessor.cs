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
        //private Dictionary<long, List<Label>> metricLabels;
        //private LabelDict metricStorage;
        private string connString;
        private MySqlConnection sqlConnection;
        private List<ZabbixMetric> notKnown = new List<ZabbixMetric>();
        private Timer _getNotKnown;
        private readonly SemaphoreSlim _semaphore;
        private List<Regex> regexps = new List<Regex>();
        private Regex notDefinedRex = new Regex(".*\\[(.*)\\]", RegexOptions.Compiled);
        private readonly IFormatProvider culture = new System.Globalization.CultureInfo("en-US");
        private int zbxVersion;

        public ZabbixMetricProcessor(IConfiguration config, ILogger<ZabbixMetricProcessor> logger, IZabbixMetricQueue zabbixMetricQueue, ITimeSeriesQueue timeSeriesQueue)
        {
            _logger = logger;
            zbxConfig = config.GetSection("zabbix");

            zbxQueue = zabbixMetricQueue;
            tsQueue = timeSeriesQueue;
            zbxVersion = zbxConfig.GetValue<int>("version", 3);
            var connStrBuild = new MySqlConnectionStringBuilder
            {
                Port = zbxConfig.GetValue<uint>("port"),
                Server = zbxConfig.GetValue<string>("hostname"),
                UserID = zbxConfig.GetValue<string>("username"),
                Password = zbxConfig.GetValue<string>("password"),
                Database = zbxConfig.GetValue<string>("database"),
                ConnectionTimeout = 600
            };
            var confRegexps = zbxConfig.GetSection("metricRegexp").AsEnumerable();
            foreach (var regex in confRegexps)
            {
                if (String.IsNullOrEmpty(regex.Value))
                    continue;
                regexps.Add(new Regex(regex.Value, RegexOptions.Compiled));
            }
            connString = connStrBuild.ConnectionString;
            //metricLabels = new Dictionary<long, List<Label>>(100000);
            metricStorage = new LabelDict();
            _semaphore = new SemaphoreSlim(1, 1);
            _getNotKnown = new Timer(GetNotKnownAsync, null, TimeSpan.Zero, TimeSpan.FromSeconds(10));
        }

        private async void GetNotKnownAsync(object state)
        {
            if (_semaphore.Wait(100))
            {
                try
                {
                    if (notKnown.Count > 0)
                    {
                        var toWork = new List<ZabbixMetric>();
                        lock (notKnown)
                        {
                            toWork = new List<ZabbixMetric>(notKnown);
                            notKnown.Clear();
                        }
                        var distinctLabels = toWork.Select(x => x.itemId).Distinct();

                        var newLabels = await GetMetricLabelsAsync(distinctLabels);
                        //metricLabels.EnsureCapacity(metricLabels.Count + newLabels.Count);
                        //newLabels.ToList().ForEach(x => metricLabels[x.Key] = x.Value);
                        newLabels.ToList().ForEach(x => metricStorage.StoreLabels(x.Key, x.Value)); 
                        foreach (var metric in toWork)
                        {
                            ProcessMetrics(metric);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while getting not known");
                }
                finally
                {
                    _semaphore.Release();
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

        private async Task<Dictionary<long, List<Label>>> GetMetricLabelsAsync (IEnumerable<long> ids)
        {
            var result = new Dictionary<long, List<Label>>(ids.Count());
            var conn = GetConnection();
            var cmd = conn.CreateCommand();
            cmd.CommandTimeout = 600;
            cmd.CommandText = zbxVersion >= 4 ? selectAll_v4 : selectAll;
            cmd.Parameters.AddWithValue("ids", string.Join(',', ids));
            using (var reader = await cmd.ExecuteReaderAsync()) 
                while (await reader.ReadAsync())
                {
                    var id = reader.GetInt64(0);
                    var labels = new List<Label>();
                    for (int i = 1; i < reader.FieldCount; i++)
                    {
                        if (!reader.IsDBNull(i))
                        {
                            var name = reader.GetName(i);
                            var value = reader.GetString(i);

                            if (name == "__name__")
                            {
                                if (value.StartsWith("zabbix"))
                                {
                                    value = value.Replace("[", "_").Replace("]", "_").Replace(",", "_").Replace(" ", "_").Replace("__", "_").Trim('_');
                                }
                                else
                                {
                                    try
                                    {
                                        bool processed = true;
                                        foreach (var regex in regexps)
                                        {
                                            var match = regex.Match(value);
                                            if (match.Groups.Count > 1)
                                            {
                                                for (int j = 1; j < match.Groups.Count; j++)
                                                {
                                                    var group = match.Groups[i];
                                                    labels.Add(new Label(group.Name, group.Value));
                                                }
                                            }
                                        }
                                        if (!processed)
                                        {
                                            var match = notDefinedRex.Match(value);
                                            if (match.Groups.Count > 1)
                                            {
                                                int iter = 0;
                                                foreach (var addLabel in match.Groups[1].Value.Split(','))
                                                {
                                                    labels.Add(new Label($"label_{iter}", addLabel.Trim()));
                                                }
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogError(ex, "Error while parsing regexp");
                                    }
                                }

                                value = value.Replace("[", "_").Replace("]", "_").Replace(",", "_").Replace(" ", "_").Replace("__", "_").Trim('_');
                            }
                            labels.Add(new Label(name, value));
                        }
                    }
                    result.Add(id, labels);
                }
            return result;
        }

        private async Task DoWork(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                SpinWait.SpinUntil(() => { return notKnown.Count < 2000 || stoppingToken.IsCancellationRequested; });
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
            var labels = metricStorage.GetLabels(metric.itemId);
            //if (metricLabels.ContainsKey(metric.itemId))
            if (labels != null)
            {
                var ts = new TimeSeries();
                //ts.SetLabels(metricLabels[metric.itemId]);
                ts.SetLabels(labels);
                ts.AddSample(metric.time, metric.value);
                tsQueue.Enqueue(ts);
            }
            else
            {
                lock (notKnown)
                {
                    notKnown.Add(metric);
                }
                
            }

        }

        private MySqlConnection GetConnection()
        {
            if (sqlConnection == null)
            {
                sqlConnection = new MySqlConnection(connString);
                sqlConnection.Open();
            }
            else
            {
                sqlConnection.Ping();
                if (sqlConnection.State != System.Data.ConnectionState.Open)
                {
                    sqlConnection = new MySqlConnection(connString);
                    sqlConnection.Open();
                }
            }
            return sqlConnection;
        }



        //itm.itemid, itm.templateid, itm.interfaceid, hst.proxy_hostid, itm.state, itm.type item_type, itm.description item_descr,
        private string selectAll_v4 = @"SELECT itm.itemid, 
itm.name item_name, itm.key_ __name__, 
hst.host host_host , hst.name host_name,
prx.host proxy_host, prx.name proxy_name, hgrps.groups host_groups, hiface.dns dns, hiface.ip ips, tmpl.name template, apps.name apps
FROM items itm
LEFT JOIN hosts hst ON hst.hostid = itm.hostid 
LEFT JOIN hosts prx ON hst.proxy_hostid = prx.hostid
LEFT JOIN (SELECT hostid, group_concat(name separator';') ""groups"" FROM zabbix.hosts_groups hgr JOIN zabbix.hstgrp gr ON hgr.groupid = gr.groupid WHERE gr.internal != 1 GROUP BY hostid) hgrps
  ON hgrps.hostid = itm.hostid
LEFT JOIN(SELECT hostid, group_concat(dns separator';') dns, group_concat(ip separator';') ip FROM zabbix.interface GROUP BY hostid ) hiface
 ON hiface.hostid = itm.hostid
LEFT JOIN(SELECT itemid tmplid, hosts.name name FROM items JOIN hosts ON hosts.hostid = items.hostid) tmpl ON tmpl.tmplid = itm.templateid
LEFT JOIN(SELECT hostid, group_concat(name separator';') name FROM applications GROUP BY hostid) apps ON apps.hostid = itm.hostid
WHERE value_type IN(0, 3) AND FIND_IN_SET(itemid, @ids) != 0";
        private string selectAll = @"SELECT itm.itemid, 
itm.name item_name, itm.key_ item_key, 
hst.host host_host , hst.name host_name,
prx.host proxy_host, prx.name proxy_name, hgrps.groups host_groups, hiface.dns dns, hiface.ip ips, tmpl.name template, apps.name apps
FROM items itm
LEFT JOIN hosts hst ON hst.hostid = itm.hostid 
LEFT JOIN hosts prx ON hst.proxy_hostid = prx.hostid
LEFT JOIN (SELECT hostid, group_concat(name separator';') ""groups"" FROM zabbix.hosts_groups hgr JOIN zabbix.groups gr ON hgr.groupid = gr.groupid WHERE gr.internal != 1 GROUP BY hostid) hgrps
  ON hgrps.hostid = itm.hostid
LEFT JOIN(SELECT hostid, group_concat(dns separator';') dns, group_concat(ip separator';') ip FROM zabbix.interface GROUP BY hostid ) hiface
 ON hiface.hostid = itm.hostid
LEFT JOIN(SELECT itemid tmplid, hosts.name name FROM items JOIN hosts ON hosts.hostid = items.hostid) tmpl ON tmpl.tmplid = itm.templateid
LEFT JOIN(SELECT hostid, group_concat(name separator';') name FROM applications GROUP BY hostid) apps ON apps.hostid = itm.hostid
WHERE value_type IN(0, 3) AND FIND_IN_SET(itemid, @ids) != 0 LIMIT 1000";

        
    }
}