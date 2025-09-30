using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MySqlX.XDevAPI.Relational;
using Npgsql;
using Npgsql.Replication;
using Npgsql.Replication.PgOutput;
using Npgsql.Replication.PgOutput.Messages;
using Npgsql.Replication.TestDecoding;
using NpgsqlTypes;
using Sitescope2RemoteWrite.Helpers;
using Sitescope2RemoteWrite.Models;
using Sitescope2RemoteWrite.PromPb;
using Sitescope2RemoteWrite.Queueing;
using Sitescope2RemoteWrite.Storage;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
        //private PhysicalReplicationConnection client;
        //private readonly IReplicationStateStorage replStateStorage;
        private readonly string slotName;
        private readonly string publicationName;
        private readonly bool useStreamingReplication;
        private bool startFromFirst = false;
        private PgOutputReplicationSlot slot;
        //private PhysicalReplicationSlot slot;

        public ZabbixPullerPostgreSQL(IServiceProvider services, ILogger<ZabbixPullerPostgreSQL> logger, IZabbixMetricQueue zabbixMetric, IConfiguration configuration) //, IReplicationStateStorage replicationState)
        {
            _logger = logger;
            //replStateStorage = replicationState;
            metricQueue = zabbixMetric;
            zpullConfig = configuration.GetSection("zabbix");
            slotName = zpullConfig.GetValue<string>("replSlot", "vmrepl");
            publicationName = zpullConfig.GetValue<string>("publication", "items");
            useStreamingReplication = zpullConfig.GetValue<bool>("streaming", false);
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
                    //var lastState = replStateStorage.GetLastState();
                    var connStrBuild = new NpgsqlConnectionStringBuilder
                    {
                        Port = zpullConfig.GetValue<int>("port"),
                        Host = zpullConfig.GetValue<string>("hostname"),
                        Username = zpullConfig.GetValue<string>("username"),
                        Password = zpullConfig.GetValue<string>("password"),
                        Database = zpullConfig.GetValue<string>("database"),
                    };
                    await using var ds = new NpgsqlConnection(connStrBuild.ConnectionString);
                    await ds.OpenAsync();
                    var startLsn = NpgsqlLogSequenceNumber.Invalid;

                    client = new LogicalReplicationConnection(connStrBuild.ConnectionString);
                    await client.Open();
                    await using (var cmd = ds.CreateCommand()) {
                        cmd.CommandText = "SELECT confirmed_flush_lsn FROM pg_replication_slots WHERE slot_name = $1";
                        cmd.Parameters.AddWithValue(slotName);

                        await using (var reader = await cmd.ExecuteReaderAsync(stoppingToken))
                            if (await reader.ReadAsync())
                            {
                                if (!reader.IsDBNull(0))
                                    startLsn = reader.GetFieldValue<NpgsqlLogSequenceNumber>(0);
                                else
                                {
                                    await client.DropReplicationSlot(slotName);
                                }
                                    
                            }
                                
                    }

                    //client.Create

                    if (startLsn == NpgsqlLogSequenceNumber.Invalid || startFromFirst)
                    {
                        slot = await client.CreatePgOutputReplicationSlot(slotName);
                        //await client.CreateReplicationSlot(slotName, reserveWal: true);
                        //slot = await client.ReadReplicationSlot(slotName);
                    }
                    else
                    {
                        slot = new PgOutputReplicationSlot(new ReplicationSlotOptions(slotName, startLsn));
                        //slot = new PhysicalReplicationSlot(slotName, startLsn);
                    }
                        
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
            //IAsyncEnumerable<XLogDataMessage> iter;
            //if (slot.RestartLsn != NpgsqlLogSequenceNumber.Invalid)
            //{
            //    iter = client.StartReplication(slot, stoppingToken);
            //}
            //else
            //{
            //    iter = client.StartReplication(slot, NpgsqlLogSequenceNumber.Invalid, stoppingToken, );
            //}
            await foreach (var binlogEvent in 
                client.StartReplication(
                    slot, 
                    new PgOutputReplicationOptions(publicationName, 1, streaming: useStreamingReplication), 
                    stoppingToken
                )
            )
            {
                //var test = new TestDecodingData();
                //test.
                //Console.WriteLine(binlogEvent.ToString());
                //using var sr = new StreamReader(binlogEvent.Data);
                //Console.WriteLine(binlogEvent.GetType().ToString());
                //Console.WriteLine(sr.ReadToEnd());
                //binlogEvent
                //gtid = client.State.GtidState.ToString(),

                //Console.WriteLine($"{state.Filename}: {state.Position}");
                //state.GtidState
                if (binlogEvent is InsertMessage insert)
                {
                    //if (insert.Relation.RelationName == "history" || insert.Relation.RelationName == "history_uint")
                    {
                        var enumerator = insert.NewRow.GetAsyncEnumerator(stoppingToken);
                        var metricValue = new ZabbixMetric();
                        
                        await enumerator.MoveNextAsync();
                        metricValue.itemId = long.Parse(await enumerator.Current.Get<string>());
                        await enumerator.MoveNextAsync();
                        metricValue.time = long.Parse(await enumerator.Current.Get<string>()) * 1000;
                        await enumerator.MoveNextAsync();
                        metricValue.value = double.Parse(await enumerator.Current.Get<string>());

                        metricQueue.Enqueue(metricValue);
                    }
                }
                //RelationMessage

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