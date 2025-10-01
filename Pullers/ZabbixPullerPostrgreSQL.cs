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
using System.Data;
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
        private readonly bool useBinaryReplication;
        private readonly uint protocolVersion;
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
            useBinaryReplication = zpullConfig.GetValue<bool>("binaryReplication", false);
            protocolVersion = zpullConfig.GetValue<uint>("protocolVersion", 1);
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
                    var replayFrom = zpullConfig.GetValue<DateTime>("replayfrom", DateTime.MinValue);
                    var replayTo = zpullConfig.GetValue<DateTime>("replayto", DateTime.MaxValue);
                    if (replayFrom.Year > 1)
                    {
                        await using (var cmd = ds.CreateCommand())
                        {
                            cmd.CommandText = "SELECT itemid, clock, value FROM history WHERE clock BETWEEN $1 AND $2 ORDER BY itemid";
                            cmd.CommandTimeout = 0;
                            cmd.Parameters.AddWithValue(replayFrom.ToUnixTimeStamp());
                            cmd.Parameters.AddWithValue(replayTo.ToUnixTimeStamp());
                            await using (var reader = await cmd.ExecuteReaderAsync(stoppingToken))
                            {
                                while(await reader.ReadAsync())
                                {
                                    var metricValue = new ZabbixMetric()
                                    {
                                        itemId = reader.GetInt64(0),
                                        time = reader.GetInt64(1),
                                        value = reader.GetDouble(2),
                                    };
                                    metricQueue.Enqueue(metricValue);
                                }
                            }
                        }
                    }

                    var startLsn = NpgsqlLogSequenceNumber.Invalid;

                    client = new LogicalReplicationConnection(connStrBuild.ConnectionString);
                    //client.WalReceiverTimeout = Timeout.InfiniteTimeSpan;
                    await client.Open();
                    await using (var cmd = ds.CreateCommand()) {
                        cmd.CommandText = "SELECT confirmed_flush_lsn, wal_status FROM pg_replication_slots WHERE slot_name = $1";
                        cmd.Parameters.AddWithValue(slotName);

                        await using (var reader = await cmd.ExecuteReaderAsync(stoppingToken))
                            if (await reader.ReadAsync())
                            {
                                if (reader.GetFieldValue<string>(1) != "lost")
                                    startLsn = reader.GetFieldValue<NpgsqlLogSequenceNumber>(0);
                                else
                                {
                                    await client.DropReplicationSlot(slotName);
                                    _logger.LogError("Replica lost. Restarting");
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
                finally
                {
                    if (client != null)
                        await client.DisposeAsync();
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
            var counter = 0;
            var debug = false;

            if (_logger.IsEnabled(LogLevel.Debug))
            {
                debug = true;
                var periodS = 5;
                var timer = new Timer((aaa) =>
                {
                    //lock (locker)
                    {
                        Console.WriteLine($"{counter / periodS}/s avg");
                        counter = 0;
                    }
                }, null, 0, periodS * 1000);
            }
            
            await foreach (var binlogEvent in 
                client.StartReplication(
                    slot, 
                    new PgOutputReplicationOptions(
                        publicationName, 
                        protocolVersion: protocolVersion, 
                        streaming: useStreamingReplication,
                        binary: useBinaryReplication
                    ), 
                    stoppingToken
                )
            )
            {
                try
                {
                    if (debug)
                        counter++;
                    //client.SetReplicationStatus(binlogEvent.WalEnd);
                    //continue;
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

                        if (insert.Relation.RelationName.StartsWith("_hyper_1") || insert.Relation.RelationName.StartsWith("_hyper_2"))
                        {
                            var enumerator = insert.NewRow.GetAsyncEnumerator(stoppingToken);
                            var metricValue = new ZabbixMetric();
                            if (!useBinaryReplication)
                            {
                                await enumerator.MoveNextAsync();
                                metricValue.itemId = long.Parse(await enumerator.Current.Get<string>());
                                await enumerator.MoveNextAsync();
                                metricValue.time = long.Parse(await enumerator.Current.Get<string>()) * 1000;
                                await enumerator.MoveNextAsync();
                                metricValue.value = double.Parse(await enumerator.Current.Get<string>());
                            }
                            else
                            {
                                await enumerator.MoveNextAsync();
                                metricValue.itemId = await enumerator.Current.Get<long>();
                                await enumerator.MoveNextAsync();
                                metricValue.time = await enumerator.Current.Get<long>() * 1000;
                                await enumerator.MoveNextAsync();
                                metricValue.value = await enumerator.Current.Get<double>();
                            }

                            metricQueue.Enqueue(metricValue);
                        }
                    }
                    client.SetReplicationStatus(binlogEvent.WalEnd);
                    //RelationMessage

                }
                catch (Exception ex) {
                    _logger.LogError("aa", ex);
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