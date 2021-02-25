using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MySql.Data.MySqlClient;
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


    public class ReplicationState
    {
        public string filename;
        public long position;
        public string gtid;
    }

    public class ReplicationStateStorage : IHostedService // : IReplicationStateStorage
    {
        private readonly ILogger<ReplicationStateStorage> _logger;
        private readonly IConfiguration replStateConf;
        private readonly string connString;
        private readonly string stateTable;
        private MySqlConnection sqlConnection;

        private int savePeriod = 10;
        private SemaphoreSlim _semaphore;
        private ReplicationState lastState;
        private Timer _timer;

        public ReplicationStateStorage(ILogger<ReplicationStateStorage> logger, IConfiguration configuration)
        {
            _logger = logger;
            replStateConf = configuration.GetSection("zabbix");
            var connStrBuild = new MySqlConnectionStringBuilder
            {
                Port = replStateConf.GetValue<uint>("port"),
                Server = replStateConf.GetValue<string>("hostname"),
                UserID = replStateConf.GetValue<string>("username"),
                Password = replStateConf.GetValue<string>("password"),
                Database = replStateConf.GetValue<string>("database"),
            };
            connString = connStrBuild.ConnectionString;
            stateTable = replStateConf.GetValue<string>("state");
            CheckOrCreateTable();
            _logger.LogInformation("created");
            _semaphore = new SemaphoreSlim(1, 1);
        }

        private void CheckOrCreateTable()
        {
            var conn = GetConnection();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @$"CREATE TABLE IF NOT EXISTS {stateTable} (
    filename VARCHAR(128),
	position BIGINT,
	gtid VARCHAR(128)
)";
                cmd.ExecuteNonQuery();
            }
                
        }

        public ReplicationState GetLastState()
        {
            using (var cmd = GetConnection().CreateCommand())
            {
                cmd.CommandText = $"SELECT filename, position, gtid FROM {stateTable} LIMIT 1";
                using (var reader = cmd.ExecuteReader())
                    if (reader.Read())
                    {
                        var result = new ReplicationState()
                        {
                            filename = !reader.IsDBNull(0) ? reader.GetString(0) : null,
                            position = !reader.IsDBNull(1) ? reader.GetInt64(1) : 0,
                            gtid =     !reader.IsDBNull(2) ? reader.GetString(2) : null,
                        };
                        return result;
                    }
                    else
                    {
                        return null;
                    }
                
            }
        }

        public void SaveState(string filename, long position, string gtid )
        {
            lastState = new ReplicationState
            {
                filename = filename,
                position = position,
                gtid = gtid
            };
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


        private void DoSave(object state)
        {
            if (lastState == null)
                return;
            if (_semaphore.Wait(100))
            {
                try
                {
                    var conn = GetConnection();
                    using (var trx = conn.BeginTransaction())
                    using (var cmd = conn.CreateCommand())
                    {

                        cmd.Transaction = trx;
                        cmd.CommandText = $"DELETE FROM {stateTable}";
                        var changed = cmd.ExecuteNonQuery();
                        if (changed > 1)
                            throw new ArgumentException($"{stateTable} contains more than one line. It's not possible");
                        cmd.CommandText = $"INSERT INTO {stateTable} (filename, position, gtid) VALUES (@filename, @position, @gtid)";
                        cmd.Parameters.AddWithValue("filename", lastState.filename);
                        cmd.Parameters.AddWithValue("position", lastState.position);
                        cmd.Parameters.AddWithValue("gtid", lastState.gtid);
                        cmd.ExecuteNonQuery();
                        trx.Commit();
                        _logger.LogInformation($"Save completed, data: {Newtonsoft.Json.JsonConvert.SerializeObject(lastState)}");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while saving replication state");
                }
                finally
                {
                    _semaphore.Release();
                }
            }
            
        }


        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("RemoteWrite sender started");
            _timer = new Timer(DoSave, null, TimeSpan.Zero, TimeSpan.FromSeconds(savePeriod));
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("RemoteWrite sender is stopping");

            _timer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }
    }
}