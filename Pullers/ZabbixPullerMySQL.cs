using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
using MySqlCdc;
using MySqlCdc.Constants;
using MySqlCdc.Events;
using MySqlCdc.Providers.MariaDb;
using MySqlCdc.Providers.MySql;
using Sitescope2RemoteWrite.Helpers;
using Sitescope2RemoteWrite.Models;
using Sitescope2RemoteWrite.PromPb;
using Sitescope2RemoteWrite.Queueing;
using Sitescope2RemoteWrite.Storage;
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

    public class ZabbixPullerMySQL : BackgroundService
    {
        private readonly ILogger<ZabbixPullerMySQL> _logger;
        
        IZabbixMetricQueue metricQueue;
        private readonly IDebugQueue debugQueue;
        private readonly IConfiguration zpullConfig;

        private readonly List<long> allowedTables = new List<long>();
        private BinlogClient client;
        private readonly IReplicationStateStorage replStateStorage;
        private bool startFromFirst = false;

        public ZabbixPullerMySQL(IServiceProvider services, ILogger<ZabbixPullerMySQL> logger, IZabbixMetricQueue zabbixMetric, IConfiguration configuration, IReplicationStateStorage replicationState)
        {
            _logger = logger;
            replStateStorage = replicationState;
            metricQueue = zabbixMetric;
            zpullConfig = configuration.GetSection("zabbix");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {

            _logger.LogInformation(
                "Zabbix puller started"
            );
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var lastState = replStateStorage.GetLastState();


                    client = new BinlogClient(options =>
                    {
                        options.Hostname = zpullConfig.GetValue<string>("hostname");
                        options.Port = zpullConfig.GetValue<int>("port");
                        options.Username = zpullConfig.GetValue<string>("username");
                        options.Password = zpullConfig.GetValue<string>("password");
                        options.SslMode = SslMode.DISABLED;
                        options.HeartbeatInterval = TimeSpan.FromSeconds(30);
                        options.Blocking = true;
                        if (!String.IsNullOrEmpty(lastState?.filename))
                        {
                            options.Binlog = BinlogOptions.FromPosition(lastState.filename, lastState.position);
                        }
                        else
                        {
                            options.Binlog = BinlogOptions.FromStart();
                        }
                        if (startFromFirst)
                        {
                            options.Binlog = BinlogOptions.FromStart();
                            startFromFirst = false;
                        }
                            

                    });
                    await DoWork(stoppingToken);
                }
                catch (InvalidOperationException ex)
                {
                    if (ex.Message.Contains("ErrorCode: 1236"))
                    {
                        startFromFirst = true;
                        _logger.LogWarning("Starting replication from first file");
                    }
                    else
                    {
                        _logger.LogError(ex, "Error while pulling from binlog");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while pulling from binlog");
                }
            }
            
            _logger.LogInformation(
                "Zabbix puller stopped"
            );
        }

        private async Task DoWork(CancellationToken stoppingToken)
        {
            await foreach (var binlogEvent in client.Replicate(stoppingToken))
            {
                //gtid = client.State.GtidState.ToString(),
                
                //Console.WriteLine($"{state.Filename}: {state.Position}");
                //state.GtidState

                if (binlogEvent is TableMapEvent tableMap)
                {
                    replStateStorage.SaveState(client.State.Filename, client.State.Position, null);
                    if (tableMap.TableName == "history" || tableMap.TableName == "history_uint")
                    {
                        if (!allowedTables.Contains(tableMap.TableId))
                            allowedTables.Add(tableMap.TableId);
                    }
                }
                else if (binlogEvent is WriteRowsEvent writeRows)
                {
                    if (allowedTables.Contains(writeRows.TableId))
                    {
                        foreach (var row in writeRows.Rows)
                        {
                            try
                            {
                                var metricValue = new ZabbixMetric
                                {
                                    itemId = (long)row.Cells[0],
                                    value = Convert.ToDouble(row.Cells[2]),
                                    time = Convert.ToInt64(row.Cells[1])*1000
                                };
                                metricQueue.Enqueue(metricValue);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "While parsing row");
                            }
                        }
                    }
                    //ProcessMonitors();
                }
            }
        }

        private void ProcessMonitors()
        {
            
            //var timeSeries = RegexProcess.ProcessCounters(baseTS, monitor);
            //timeSeriesQueue.EnqueueList(timeSeries);
        }
    }

}