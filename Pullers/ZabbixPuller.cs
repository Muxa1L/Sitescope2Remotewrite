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

    public class ZabbixPuller : BackgroundService
    {
        private readonly ILogger<ZabbixPuller> _logger;
        
        IZabbixMetricQueue metricQueue;
        private readonly IDebugQueue debugQueue;
        private readonly IConfiguration zpullConfig;

        private readonly List<long> allowedTables = new List<long>();
        private BinlogClient client;
        private readonly ReplicationStateStorage replStateStorage;
        private ZabbixDictionary metricDict;

        public ZabbixPuller(IServiceProvider services, ILogger<ZabbixPuller> logger, IZabbixMetricQueue zabbixMetric, IConfiguration configuration, ReplicationStateStorage replicationState)
        {
            _logger = logger;
            replStateStorage = replicationState;
            metricQueue = zabbixMetric;
            zpullConfig = configuration.GetSection("zabbix");
            metricDict = new ZabbixDictionary(zpullConfig);
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
                ///TODO remove this before release
                //options.Binlog = BinlogOptions.FromStart();
                /*if (lastState != null)
                {
                    if (!String.IsNullOrEmpty(lastState.filename))
                    {
                        options.Binlog = BinlogOptions.FromPosition(lastState.filename, lastState.position);
                    }
                    else
                    {
                        options.Binlog = BinlogOptions.FromStart();
                    }
                }
                else
                {
                    options.Binlog = BinlogOptions.FromStart();
                }*/
                //options.Binlog = BinlogOptions.FromPosition("mysql-bin.000008", 195);
                //options.Binlog = BinlogOptions.FromStart();
            });
            
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {

            _logger.LogInformation(
                "Zabbix puller started"
            );
            await DoWork(stoppingToken);
        }

        private async Task DoWork(CancellationToken stoppingToken)
        {
            await foreach (var binlogEvent in client.Replicate(stoppingToken))
            {
                //gtid = client.State.GtidState.ToString(),
                replStateStorage.SaveState(client.State.Filename, client.State.Position, null);
                //Console.WriteLine($"{state.Filename}: {state.Position}");
                //state.GtidState

                if (binlogEvent is TableMapEvent tableMap)
                {
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
                        ProcessMonitors();
                }
            }
        }

        private void ProcessMonitors()
        {
            
            //var timeSeries = RegexProcess.ProcessCounters(baseTS, monitor);
            //timeSeriesQueue.EnqueueList(timeSeries);
        }

        private class ZabbixDictionary {

            private string connString;
            private MySqlConnection sqlConnection;

            public ZabbixDictionary(IConfiguration config)
            {
                var connStrBuild = new MySqlConnectionStringBuilder
                {
                    Port = config.GetValue<uint>("port"),
                    Server = config.GetValue<string>("hostname"),
                    UserID = config.GetValue<string>("username"),
                    Password = config.GetValue<string>("password"),
                    Database = config.GetValue<string>("database"),
                };
                connString = connStrBuild.ConnectionString;
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
        }
    }

}