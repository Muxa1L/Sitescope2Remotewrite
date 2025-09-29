using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MySqlX.XDevAPI.Relational;
using Npgsql;
using Npgsql.Replication;
using Npgsql.Replication.PgOutput;
using Npgsql.Replication.PgOutput.Messages;
using NpgsqlTypes;
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

    public class ZabbixPullerPostgreSQL : BackgroundService
    {
        private readonly ILogger<ZabbixPullerPostgreSQL> _logger;
        
        IZabbixMetricQueue metricQueue;
        private readonly IDebugQueue debugQueue;
        private readonly IConfiguration zpullConfig;

        private readonly List<long> allowedTables = new List<long>();
        private LogicalReplicationConnection client;
        private readonly IReplicationStateStorage replStateStorage;
        private readonly string slotName;
        private bool startFromFirst = false;
        private PgOutputReplicationSlot slot;

        public ZabbixPullerPostgreSQL(IServiceProvider services, ILogger<ZabbixPullerPostgreSQL> logger, IZabbixMetricQueue zabbixMetric, IConfiguration configuration, IReplicationStateStorage replicationState)
        {
            _logger = logger;
            replStateStorage = replicationState;
            metricQueue = zabbixMetric;
            zpullConfig = configuration.GetSection("zabbix");
            slotName = zpullConfig.GetValue<string>("replSlot", "vmrepl");
            
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
                    var connStrBuild = new NpgsqlConnectionStringBuilder
                    {
                        Port = zpullConfig.GetValue<int>("port"),
                        Host = zpullConfig.GetValue<string>("hostname"),
                        Username = zpullConfig.GetValue<string>("username"),
                        Password = zpullConfig.GetValue<string>("password"),
                        Database = zpullConfig.GetValue<string>("database"),
                    };
                    await using var ds = new NpgsqlConnection(connStrBuild.ConnectionString);
                    var startLsn = NpgsqlLogSequenceNumber.Invalid;

                    // This is just an example and not a good practice in most cases, because your
                    // application may have processed a message but the backend may not have received
                    // or processed the feedback message where your application confirmed it before
                    // things (connection, your application, the backend) broke.
                    // This may result in your application processing messages twice after restarting
                    // which probably is not what you want.
                    // Typically, you'd maintain a database table ore some list with the LSNs of
                    // successfully processed messages in your application to which you add in a
                    // transactional way.
                    await using (var cmd = ds.CreateCommand()) {
                        cmd.CommandText = "SELECT confirmed_flush_lsn FROM pg_replication_slots WHERE slot_name = $1";
                        cmd.Parameters.AddWithValue(slot);

                        await using (var reader = await cmd.ExecuteReaderAsync(stoppingToken))
                            if (await reader.ReadAsync())
                                startLsn = reader.GetFieldValue<NpgsqlLogSequenceNumber>(0);
                    }


                    client = new LogicalReplicationConnection(connStrBuild.ConnectionString);
                    client.Open();
                    if (startLsn == NpgsqlLogSequenceNumber.Invalid || startFromFirst)
                    {
                        slot = await client.CreatePgOutputReplicationSlot(slotName);
                    }
                    else
                        slot = new PgOutputReplicationSlot(new ReplicationSlotOptions(slotName, startLsn));
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
            await foreach (var binlogEvent in client.StartReplication(
                       slot, new PgOutputReplicationOptions(slotName+"_pub", 1), stoppingToken))
            {
                //gtid = client.State.GtidState.ToString(),
                
                //Console.WriteLine($"{state.Filename}: {state.Position}");
                //state.GtidState
                if (binlogEvent is InsertMessage insert)
                {
                    if (insert.Relation.RelationName == "history" || insert.Relation.RelationName == "history_uint")
                    {
                        var enumerator = insert.NewRow.GetAsyncEnumerator(stoppingToken);
                        var metricValue = new ZabbixMetric();
                        metricValue.itemId = await enumerator.Current.Get<long>();
                        await enumerator.MoveNextAsync();
                        metricValue.time = await enumerator.Current.Get<long>();
                        await enumerator.MoveNextAsync();
                        metricValue.value = await enumerator.Current.Get<double>();
                        
                        metricQueue.Enqueue(metricValue);
                    }
                }

                client.SetReplicationStatus(binlogEvent.WalEnd);
            }
        }

        private void ProcessMonitors()
        {
            
            //var timeSeries = RegexProcess.ProcessCounters(baseTS, monitor);
            //timeSeriesQueue.EnqueueList(timeSeries);
        }
    }

}