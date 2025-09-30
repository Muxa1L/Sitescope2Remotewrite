using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Npgsql;
using Sitescope2RemoteWrite.PromPb;
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

namespace Sitescope2RemoteWrite.Storage
{
    

    public class LabelStoragePostgreSQL: ILabelStorage
    {
        //private ConcurrentDictionary<long, List<Label>> labels = new ConcurrentDictionary<long, List<Label>>();
        private LabelDict labelDict;
        private HashSet<long> notKnown = new HashSet<long>();
        private Timer _getNotKnown;
        private SemaphoreSlim _semaphore;
        private ILogger<LabelStoragePostgreSQL> _logger;
        //private int zbxVersion;
        private int maxNotKnown;
        private string connString;
        private NpgsqlConnection sqlConnection;
        private List<Regex> regexps = new List<Regex>();
        private Regex notDefinedRex = new Regex("(.*)\\[(.*)\\]", RegexOptions.Compiled);

        public LabelStoragePostgreSQL(IConfiguration config, ILogger<LabelStoragePostgreSQL> logger)
        {
            labelDict = new LabelDict();
            var zbxConfig = config.GetSection("zabbix");
            //zbxVersion = zbxConfig.GetValue<int>("version", 3);
            var connStrBuild = new NpgsqlConnectionStringBuilder
            {
                Port = zbxConfig.GetValue<int>("port"),
                Host = zbxConfig.GetValue<string>("hostname"),
                Username = zbxConfig.GetValue<string>("username"),
                Password = zbxConfig.GetValue<string>("password"),
                Database = zbxConfig.GetValue<string>("database"),
                //Timeout = 7200,
            };
            maxNotKnown = zbxConfig.GetValue<int>("maxNotKnown", 1000);
            _semaphore = new SemaphoreSlim(1, 1);
            var confRegexps = zbxConfig.GetSection("metricRegexp").AsEnumerable();
            //GetNotKnownAsync(null);
            foreach (var regex in confRegexps)
            {
                if (String.IsNullOrEmpty(regex.Value))
                    continue;
                regexps.Add(new Regex(regex.Value, RegexOptions.Compiled));
            }
            connString = connStrBuild.ConnectionString;

            _logger = logger;
            
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
                        var toWork = new HashSet<long>();
                        lock (notKnown)
                        {
                            toWork = new HashSet<long>(notKnown);
                            notKnown.Clear();
                        }
                        

                        var newLabels = await GetMetricLabelsAsync(toWork);
                        _logger.LogInformation($"Got {newLabels.Count} new labels");
                        //metricLabels.EnsureCapacity(metricLabels.Count + newLabels.Count);
                        //newLabels.ToList().ForEach(x => metricLabels[x.Key] = x.Value);
                        //newLabels.ToList().ForEach(x => labels.TryAdd(x.Key, x.Value));
                        foreach (var label in newLabels)
                        {
                            labelDict.StoreLabels(label.Key, label.Value);
                            toWork.Remove(label.Key);
                        }
                        foreach (var notFound in toWork)
                        {
                            labelDict.StoreLabels(notFound, new List<Label>());
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

        private async Task<Dictionary<long, List<Label>>> GetMetricLabelsAsync(IEnumerable<long> ids)
        {
            var result = new Dictionary<long, List<Label>>(ids.Count());
            var conn = GetConnection();
            var cmd = conn.CreateCommand();
            cmd.CommandTimeout = 7200;

            //if (labelDict.IsEmpty())
            //    cmd.CommandText = selectAll_v6;
            //else
            {
                cmd.CommandText = selectById_v6;
                cmd.CommandText = String.Format(cmd.CommandText, string.Join(',', ids));
                //cmd.Parameters.AddWithValue("ids", );
            }
            
            using (var reader = await cmd.ExecuteReaderAsync())
                while (await reader.ReadAsync())
                {
                    var id = reader.GetInt64(0);
                    var labels = new List<Label>();
                    labels.Add(new Label("itemid", id.ToString()));
                    for (int i = 1; i < reader.FieldCount; i++)
                    {
                        if (!reader.IsDBNull(i))
                        {
                            var name = reader.GetName(i);
                            var value = reader.GetString(i);
                            switch (name) {
                                case "host_groups":
                                case "dns":
                                case "ips":
                                case "apps":
                                    value = ";" + value.Trim(';') + ";";
                                    value = value.Replace(";;", "");
                                    break;
                            }
                            if (name == "__name__")
                            {
                                if (!value.StartsWith("zabbix"))
                                {
                                    try
                                    {
                                        bool processed = false;
                                        foreach (var regex in regexps)
                                        {
                                            var match = regex.Match(value);
                                            if (match.Groups.Count > 1)
                                            {
                                                processed = true;
                                                value = match.Groups[1].Value;
                                                for (int j = 2; j < match.Groups.Count; j++)
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
                                                value = match.Groups[1].Value;
                                                foreach (var addLabel in match.Groups[2].Value.Split(','))
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

                                value = value.Replace("[", "_").Replace("]", "_").Replace(",", "_").Replace(".", "_").Replace(" ", "_").Replace("__", "_").Trim('_').Trim().ToLower();
                            }
                            labels.Add(new Label(name, value));
                        }
                    }
                    result.Add(id, labels);
                }
            return result;
        }

        public List<Label> GetLabels(long id)
        {
            return labelDict.GetLabels(id);
        }

        public bool HasLabels(long id)
        {
            
            if (!labelDict.Contains(id))
            {
                lock (notKnown)
                {
                    notKnown.Add(id);
                }
                return false;
            }
            return true;
        }

        public bool QueueFull()
        {
            return notKnown.Count > maxNotKnown;
        }

        private NpgsqlConnection GetConnection()
        {
            if (sqlConnection == null)
            {
                sqlConnection = new NpgsqlConnection(connString);
                sqlConnection.Open();
            }
            else
            {
                if (sqlConnection.State != System.Data.ConnectionState.Open)
                {
                    sqlConnection = new NpgsqlConnection(connString);
                    sqlConnection.Open();
                }
            }
            return sqlConnection;
        }


        /**/
        private string selectById_v6 = @"SELECT itm.itemid, 
itm.name item_name, itm.key_ __name__, 
hst.host host_host , hst.name host_name,
prx.host proxy_host, prx.name proxy_name, hgrps.groups host_groups, hiface.dns dns, hiface.ip ips, tmpl.name template, apps.name apps
FROM items itm
LEFT JOIN hosts hst ON hst.hostid = itm.hostid 
LEFT JOIN hosts prx ON hst.proxy_hostid = prx.hostid
LEFT JOIN (SELECT hostid, STRING_AGG(name, ';' ORDER BY name ASC) ""groups"" FROM public.hosts_groups hgr JOIN public.hstgrp gr ON hgr.groupid = gr.groupid WHERE gr.internal != 1 GROUP BY hostid) hgrps
  ON hgrps.hostid = itm.hostid
LEFT JOIN(SELECT hostid, STRING_AGG(dns, ';' ORDER BY dns ASC) dns, STRING_AGG(ip, ';' ORDER BY ip ASC) ip FROM public.interface GROUP BY hostid ) hiface
 ON hiface.hostid = itm.hostid
LEFT JOIN(SELECT itemid tmplid, hosts.name name FROM items JOIN hosts ON hosts.hostid = items.hostid) tmpl ON tmpl.tmplid = itm.templateid
LEFT JOIN (SELECT itemid appitemid, STRING_AGG(value, ';' ORDER BY value ASC) name
  FROM public.item_tag itmapp
  GROUP BY itemid
) apps ON apps.appitemid = itm.itemid
WHERE value_type IN(0, 3) AND itemid IN ({0})";
        private string selectAll_v6 = @"SELECT itm.itemid, 
itm.name item_name, itm.key_ __name__, 
hst.host host_host , hst.name host_name,
prx.host proxy_host, prx.name proxy_name, hgrps.groups host_groups, hiface.dns dns, hiface.ip ips, tmpl.name template, apps.name apps
FROM items itm
LEFT JOIN hosts hst ON hst.hostid = itm.hostid 
LEFT JOIN hosts prx ON hst.proxy_hostid = prx.hostid
LEFT JOIN (SELECT hostid, STRING_AGG(name, ';' ORDER BY name ASC) ""groups"" FROM public.hosts_groups hgr JOIN public.hstgrp gr ON hgr.groupid = gr.groupid WHERE gr.internal != 1 GROUP BY hostid) hgrps
  ON hgrps.hostid = itm.hostid
LEFT JOIN(SELECT hostid, STRING_AGG(dns, ';' ORDER BY dns ASC) dns, STRING_AGG(ip, ';' ORDER BY ip ASC) ip FROM public.interface GROUP BY hostid ) hiface
 ON hiface.hostid = itm.hostid
LEFT JOIN(SELECT itemid tmplid, hosts.name name FROM items JOIN hosts ON hosts.hostid = items.hostid) tmpl ON tmpl.tmplid = itm.templateid
LEFT JOIN (SELECT itemid appitemid, STRING_AGG(value, ';' ORDER BY value ASC) name
  FROM public.item_tag itmapp
  GROUP BY itemid
) apps ON apps.appitemid = itm.itemid
WHERE value_type IN(0, 3) AND itm.status = 0";
    }
}
