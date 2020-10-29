using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.IO;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Reflection;
using MuxLibrary;
using Microsoft.Extensions.Logging;
using Confluent.Kafka;
using Prometheus;

namespace MetricTransform
{
    class Transformer
    {
        private CancellationTokenSource cancelSource = new CancellationTokenSource();
        private CancellationToken cancelToken;
        private IFormatProvider culture = new System.Globalization.CultureInfo("en-US");
        private string kafkaServers;

        private string transformTopic;
        private string loadTopic;
        private string nonExistentTopic;

        private List<Thread> threads;
        private readonly MuxLibrary.Data.vertica.DAL dal;
        //volatile int inTransform = 0;
        volatile bool needSyncCheck;
        private static readonly Gauge inTransform =
            Metrics.CreateGauge("transformer_total_in_transform", "monitors currently in transform");
        private static readonly Counter totalTransformed =
            Metrics.CreateCounter("transformer_total_transformed", "Total monitors transformed");
        private static readonly Counter newMetrics =
            Metrics.CreateCounter("transformer_new_metrics", "New metrics added");
        private static readonly Counter appErrors =
            Metrics.CreateCounter("transformer_app_errors", "Total application errors");
        private static readonly Counter kafkaErrors =
            Metrics.CreateCounter("transformer_kafka_errors", "Total errors while sending to kafka");
        //private ProducerConfig producerConfig;

        //private ConsumerConfig consumerConfig;

        private ClientConfig clientConfig;
        private Dictionary<string, long> metricIds;
        private DateTime maxMetricTime;

        //private Dictionary<string, MuxLibrary.Data.MetricFull> nonExistentMetrics;

        private readonly ILogger logger;

        

        private int threadCount;

        public Transformer(Microsoft.Extensions.Configuration.IConfiguration config, ILogger _logger)
        {
            logger = _logger;
            kafkaServers = config["kafka:bootstrapservers"];
            transformTopic = config["kafka:transformTopic"];
            loadTopic = config["kafka:loadTopic"];
            nonExistentTopic = config["kafka:nonExistentTopic"];
            maxMetricTime = DateTime.MinValue;
            try{
                threadCount = int.Parse(config["threadCount"]);
            }
            catch (Exception){
                threadCount = 1;
            }
            dal = new MuxLibrary.Data.vertica.DAL(
                verticaConnString: config["Vertica:verticaConnString"],
                dbname: config["Vertica:dbName"],
                metricTable: config["Vertica:metricTable"],
                valueTable: config["Vertica:valueTable"]
            );

            //nonExistentMetrics = new Dictionary<string, MuxLibrary.Data.MetricFull>();
            threads = new List<Thread>();
        }

        public void Start(Microsoft.Extensions.Configuration.IConfiguration config)
        {
            logger.LogInformation("Transformer started");
            dal.GetMetricBook(out metricIds, out maxMetricTime);
            logger.LogInformation($"Got {metricIds.Count} metrics with last created at {maxMetricTime}");
            clientConfig = new ClientConfig(){
                BootstrapServers = kafkaServers,
                //ClientId = System.Net.Dns.GetHostName(),
                Acks = Acks.Leader
            };
            

            for (int i = 0; i< threadCount; i++){
                var consumerThr = new Thread(Consumer);
                consumerThr.Name = $"consumer_{i}";
                consumerThr.Start();
                threads.Add(consumerThr);
            }

            var nonExistentCreator = new Thread(NonExistentConsumer){
                Name = $"nonexistent_0"
            };
            nonExistentCreator.Start();
            threads.Add(nonExistentCreator);
            cancelToken = cancelSource.Token;
        }

        private void Consumer(){
            var producerConfig = new ProducerConfig(clientConfig.ToDictionary(x => x.Key, x=> x.Value)){
                //TransactionalId = Guid.NewGuid().ToString(),
                ClientId = System.Net.Dns.GetHostName() + "_" + Thread.CurrentThread.Name
            };
            var consumerConfig = new ConsumerConfig(clientConfig.ToDictionary(x => x.Key, x=> x.Value)){
                GroupId = "Transformers",
                AutoOffsetReset = AutoOffsetReset.Earliest,
                //EnableAutoCommit = false,
                ClientId = System.Net.Dns.GetHostName() + "_" + Thread.CurrentThread.Name
            };
            using (var consumer = new ConsumerBuilder<Ignore, byte[]>(consumerConfig).Build())
            using (var producer = new ProducerBuilder<Null, String>(producerConfig).Build()){
                consumer.Subscribe(transformTopic);
                //producer.InitTransactions(TimeSpan.FromMinutes(1));
                while (!cancelToken.IsCancellationRequested){
                    try{
                        var consumeResult = consumer.Consume(cancelToken);
                        var messBdy = consumeResult.Message.Value;
                        if (messBdy.IsGZip())
                            messBdy = messBdy.Decompress();
                        string jsonified = Encoding.UTF8.GetString(messBdy);
                        var raw = JsonConvert.DeserializeObject<MuxLibrary.Data.Monitor>(jsonified);
                        inTransform.Inc();
                        try
                        {
                            var metrics = TransformMetrics(raw);
                            SendMetrics(metrics, producer);
                        }
                        catch (Exception ex)
                        {
                            logger.LogError($"Ошибка при обработке {ex.Message} - {ex.InnerException} \r\n {ex.StackTrace} \r\n {jsonified}");
                        }
                        inTransform.Dec();
                        totalTransformed.Inc();
                        logger.LogDebug(consumeResult.Offset.ToString());
                    }
                    catch (Exception ex){
                        logger.LogError("Error while consuming {0}\r\n{1}", ex.Message, ex.StackTrace);
                    }
                }
            }
            
        }


        private void NonExistentConsumer(){
            var nonExistentMetrics = new List<MuxLibrary.Data.MetricFull>(10000);
            var lastRun = DateTime.Now;
            var producerConfig = new ProducerConfig(clientConfig.ToDictionary(x => x.Key, x=> x.Value)){
                //TransactionalId = Guid.NewGuid().ToString(),
                ClientId = System.Net.Dns.GetHostName() + "_" + Thread.CurrentThread.Name
            };

            var consumerConfig = new ConsumerConfig(clientConfig.ToDictionary(x => x.Key, x=> x.Value)){
                GroupId = "NonExistentCreator",
                AutoOffsetReset = AutoOffsetReset.Earliest,
                //EnableAutoCommit = false,
                ClientId = System.Net.Dns.GetHostName() + "_" + Thread.CurrentThread.Name
            };
            using (var consumer = new ConsumerBuilder<Ignore, String>(consumerConfig).Build())
            using (var producer = new ProducerBuilder<Null, String>(producerConfig).Build()){
                consumer.Subscribe(nonExistentTopic);
                //producer.InitTransactions(TimeSpan.FromMinutes(1));
                while (!cancelToken.IsCancellationRequested){
                    try{
                        var consumeResult = consumer.Consume(1000);
                        if (consumeResult != null){
                            logger.LogDebug(consumeResult.Offset.ToString());
                            
                            string jsonified = consumeResult.Message.Value;
                            
                            var _metric = JsonConvert.DeserializeObject<MuxLibrary.Data.MetricFull>(jsonified);
                            nonExistentMetrics.Add(_metric);
                            newMetrics.Inc();
                        }
                        else{
                            //Console.WriteLine(consumeResult.Value);
                        }
                        
                        try
                        {
                            //nonExistentMetrics.Add(metric);
                            if (((DateTime.Now - lastRun).TotalSeconds > 10 && nonExistentMetrics.Count > 0) || nonExistentMetrics.Count > 10000){
                                lastRun = DateTime.Now;
                                var fileName = Guid.NewGuid() + ".dta";
                                var file = File.CreateText(fileName);
                                var uniqueValues = nonExistentMetrics
                                    //.Select(x => x.Value)
                                    .GroupBy(pair => (pair.Path + ":" + pair.Target + ":" + pair.MetricName))
                                    .Select(group => group.First())
                                    .ToDictionary(pair => (pair.Path + ":" + pair.Target + ":" + pair.MetricName), pair => pair);
                                logger.LogInformation($"Adding {uniqueValues.Count} new metrics to vertica");
                                foreach (var value in uniqueValues)
                                {
                                    if (metricIds.ContainsKey(value.Key)){
                                        continue;
                                    }
                                    var metr = value.Value;
                                    if (metr != null)
                                        file.WriteLine($"\"{metr.Path}\"|\"{metr.Name}\"|\"{metr.Type}\"|\"{metr.Target}\"|\"{metr.TargetIP}\"|\"{metr.SourceTemplateName}\"|" +
                                            $"\"{metr.TMID}\"|\"{metr.MetricName}\"|\"{metr.MetricUnit}\"|\"{metr.MonitoringModule}\"|\"{metr.MR}\"|\"{metr.SystemName}\"|" +
                                            $"\"{metr.ServerTM}\"|\"{metr.TemplateName}\"|\"{metr.Instance}\"");//.Replace("\\", "\\\\\\\\"));
                                                                                                                /*file.WriteLine((metr.Path + "|" + metr.Name + "|" + metr.Type + "|" + metr.Target + "|" + metr.TargetIP + "|" + metr.SourceTemplateName + "|"
                                                                                                                    + metr.TMID + "|" + metr.MetricName + "|" + metr.MetricUnit + "|" + metr.MonitoringModule + "|" + metr.MR + "|" + metr.SystemName + "|"
                                                                                                                    + metr.ServerTM + "|" + metr.TemplateName + "|" + metr.Instance).Replace("\\", "\\\\"));*/
                                }
                                file.Flush();
                                file.Close();
                                bool dups = false;
                                try
                                {
                                    dal.CreateMetrics(fileName);
                                }
                                catch(Exception ex)
                                {
                                    logger.LogError($"Error on metricCreate {ex.Message}");
                                    dups = true;
                                }
                                finally{
                                    File.Delete(fileName);
                                }
                                
                                dal.GetMetricBook(out var newMetrics, out var newMaxMetricTime, maxMetricTime);
                                if (newMetrics.Count == 0 && dups){
                                    dal.GetMetricBook(out newMetrics, out newMaxMetricTime, maxMetricTime.AddHours(-5));
                                }
                                logger.LogInformation($"Got {newMetrics.Count} metrics with last created at {newMaxMetricTime} old {maxMetricTime}");
                                lock (metricIds)
                                {
                                    if (newMaxMetricTime > maxMetricTime)
                                        maxMetricTime = newMaxMetricTime;
                                    else {
                                        Console.WriteLine(newMaxMetricTime);
                                    }
                                    metricIds.EnsureCapacity(newMetrics.Count);
                                    foreach (var kv in newMetrics){
                                        if (!metricIds.ContainsKey(kv.Key)){
                                            metricIds.Add(kv.Key, kv.Value);
                                        }
                                    }   
                                }
                                SendMetrics(nonExistentMetrics, producer);
                                lock (nonExistentMetrics){
                                    nonExistentMetrics.Clear();
                                }
                                File.Delete(fileName);
                            }
                            
                        }
                        catch (Exception ex)
                        {
                            logger.LogError($"Ошибка при обработке {ex.Message} - {ex.InnerException}  \r\n {ex.StackTrace}");
                        }
                        
                    }
                    catch (Exception ex){
                        logger.LogError("Error while consuming {0}\r\n{1}", ex.Message, ex.StackTrace);
                    }
                }
            }
            
        }

        private void SendMetrics(List<MuxLibrary.Data.MetricFull> metrics, IProducer<Null, String> producer){
            foreach (var metric in metrics){
                var key = $"{metric.Path}:{metric.Target}:{metric.MetricName}";
                if (metricIds.TryGetValue(key, out long id))
                {
                    var toSend = new MuxLibrary.Data.Metric
                    {
                        MetricId = id,
                        time = metric.Time
                        //value = metric.value
                    };
                    var val = TryParseNumber(metric.value.Replace("%", "").Replace("ms", "").Replace(",", ".").Replace("NaN", "n/a"), culture);
                    if (val == double.MinValue)
                    {
                        toSend.value_string = metric.value;
                    }
                    else
                    {
                        toSend.value_double = val;
                    }
                    var data = JsonConvert.SerializeObject(toSend, new JsonSerializerSettings{NullValueHandling = NullValueHandling.Include});
                    producer.Produce(loadTopic, new Message<Null, String> {
                        Value = data
                    }, report => {
                    if (report.Error.IsError) 
                        {
                            logger.LogError ($"Error when sending to kafka {report.Error} {report.Message}");
                            kafkaErrors.Inc();
                        }
                    });
                }
                else
                {
                    producer.Produce(nonExistentTopic, new Message<Null, String> {
                        Value = JsonConvert.SerializeObject(metric)
                    }, report => {
                    if (report.Error.IsError) 
                        {
                            logger.LogError ($"Error when sending to kafka {report.Error} {report.Message}");
                            kafkaErrors.Inc();
                        }
                    });
                }
            }
        }

        public void Stop()
        {
            if (threads.Any(thr => thr.IsAlive))
            {
                cancelSource.Cancel();
                SpinWait.SpinUntil(() => { return (inTransform.Value == 0); });
                //CreateNewMetrics(null);
                
                //rmqConnection.Close();
                
            }
        }

        private static double TryParseNumber(string input, IFormatProvider culture)
        {
            double result;
            if (double.TryParse(input, System.Globalization.NumberStyles.Float, culture, out result))
                return result;
            else
                return double.MinValue;
        }

        private List<MuxLibrary.Data.MetricFull> TransformMetrics(MuxLibrary.Data.Monitor mon)
        {
            List< MuxLibrary.Data.MetricFull> result;
            if (mon.Counters != null)
                result = new List<MuxLibrary.Data.MetricFull>(mon.Counters.Count);
            else 
                result = new List<MuxLibrary.Data.MetricFull>();
            var monData = new MuxLibrary.Data.MetricFull();
            monData.Path = mon.path;
            monData.Name = mon.name;
            monData.Type = mon.type;
            monData.Target = mon.target.ToLower();
            monData.TargetIP = mon.targetIP.ToLower();
            monData.SourceTemplateName = mon.sourceTemplateName;
            monData.Time = mon.time;
            var path = monData.Path.Split('/');
            monData.MonitoringModule = path[0].ToLower();
            monData.MR = path[1].Replace(" - Custom", "").ToUpper();
            //monData.SystemName = path.Length >= 2 ? path[2]:null;
            switch (path.Length)
            {
                case 3:
                    monData.Instance = path[2];
                    break;
                case 5:
                    monData.SystemName = path[2];
                    monData.Instance = path[3];
                    break;
                case 6:
                    monData.SystemName = path[2];
                    monData.ServerTM = path[3].ToLower();
                    monData.TemplateName = path[4];
                    monData.Instance = "[def]";
                    break;
                case 7:
                    monData.SystemName = path[2];
                    monData.ServerTM = path[3].ToLower();
                    monData.TemplateName = path[4];
                    monData.Instance = path[5];
                    break;
            }
            foreach (var cntr in mon.Counters)
            {
                try
                {
                    var metric = (MuxLibrary.Data.MetricFull)monData.Clone();
                    metric.MetricName = cntr.name;
                    metric.value = cntr.value;
                    switch (mon.name)
                    {
                        case "JSON_CustomAttr":
                            if (metric.MetricName.ToLower() == "metrics"){
                                if (cntr.value.Contains("; ")){
                                    logger.LogWarning("Борода тута!\r\n" + metric.Path);
                                }
                                var arr = cntr.value.Replace("; ", "").Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                                foreach (var metrVal in arr)
                                {
                                    try
                                    {
                                        var mvArr = metrVal.Split('=');
                                        if (mvArr.Length < 2){
                                            logger.LogWarning($"Какая-то бородатая метрика {metric.Path} - {metrVal}");
                                            continue;
                                        }
                                        var cntrMetr = (MuxLibrary.Data.MetricFull)monData.Clone();
                                        cntrMetr.MetricName = mvArr[0];
                                        cntrMetr.value = mvArr[1];
                                        result.Add(cntrMetr);
                                    }
                                    catch (Exception ex)
                                    {
                                        logger.LogWarning($"Не удалось отпарсить метрики от UniversalJson {metric.Path} - {ex.Message}");
                                    }
                                }
                            }
                            break;
                        case "OS_CPUUtil":
                            if (cntr.name.IndexOf(" cpu ") > -1) // Пропускаем счетчики вида utilization cpu # 1
                                continue;
                            break;
                        case "OS_FSMountStatus":
                        case "OS_ProcMemUtil":
                        case "OS_ProcStatus":
                        case "Win_CustomCounter":
                        case "Calls_Duration":
                        case "Calls_Outstanding":
                        case "Calls_Failed_Per_Second":
                        case "Calls_Faulted_Per_Second":
                        case "OS_ProcCPUUtil":
                            var ind3 = cntr.name.LastIndexOf('\\');
                            if (ind3 > -1)
                                metric.MetricName = cntr.name.Substring(ind3 + 1);
                            break;
                        default:
                            if (mon.name.StartsWith("VMware_") || mon.name.StartsWith("JSON_Metric") || mon.name.StartsWith("DB_Ora_Inst_Tblspc_PctUsed"))
                            {
                                var ind2 = cntr.name.LastIndexOf('/');
                                if (ind2 > -1)
                                    metric.MetricName = cntr.name.Substring(ind2 + 1);
                            }
                            if (mon.name.StartsWith("OS_WinDiskSpace")) {
                                var arr = cntr.name.Split('/', StringSplitOptions.RemoveEmptyEntries);
                                if (arr.Length >= 2){
                                    var len = arr.Length;
                                    metric.MetricName = arr[len - 2] + "/" + arr[len - 1];
                                }
                            }
                            if (mon.name.StartsWith("OS_UnixDiskSpace")) {
                                var ind4 = cntr.name.IndexOf('[');
                                if (ind4 > -1){
                                    metric.MetricName = cntr.name.Substring(ind4);
                                }
                            }
                            break;
                    }
                    /////////////////////////
                    //Кастомы для счетчиков.
                    /////////////////////////
                    switch (cntr.name)
                    {
                        case "MSMQ queues list":
                            foreach (var queue in cntr.value.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                            {
                                if (queue != "n/a")
                                    try
                                    {
                                        var qu = queue.Split('=');
                                        var cntrQueue = (MuxLibrary.Data.MetricFull)monData.Clone();
                                        cntrQueue.MetricName = "MSMQ " + qu[0];
                                        cntrQueue.value = qu[1];
                                        result.Add(cntrQueue);
                                    }
                                    catch (Exception ex)
                                    {
                                        logger.LogWarning($"Не удалось отпарсить очередь {queue} - {ex.Message}");
                                    }
                            }
                            break;
                        case "Inform":
                            if (mon.name == "UNIX_Overall_System_Load") //load average: 5.61, 6.33, 6.74
                            {
                                try
                                {
                                    var loads = cntr.value.Replace("load average: ", "").Split(',');
                                    for (int i = 0; i < 3; i++)
                                    {
                                        var cntrs = (MuxLibrary.Data.MetricFull)monData.Clone();
                                        cntrs.MetricName = "load average\\";
                                        cntrs.value = loads[i];
                                        switch (i)
                                        {
                                            case 0:
                                                cntrs.Name = cntrs.Name + "1min";
                                                break;
                                            case 1:
                                                cntrs.Name = cntrs.Name + "5min";
                                                break;
                                            case 2:
                                                cntrs.Name = cntrs.Name + "15min";
                                                break;
                                        }
                                        result.Add(cntrs);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    logger.LogWarning($"Не удалось отпарсить load averages {cntr.value} - {ex.Message}");
                                }
                            }
                            continue;
                        case "List of Counters in OK":
                            if (mon.name == "pctOfMaxConcurrentCalls")
                            {
                                var concCallCntrs = cntr.value.Split(' ');
                                foreach (var concCallCntr in concCallCntrs)
                                {
                                    try
                                    {
                                        var arr = concCallCntr.Split('|');
                                        var val = arr[arr.Length - 1].Split('=');
                                        var cntrs = (MuxLibrary.Data.MetricFull)monData.Clone();
                                        cntrs.MetricName = val[0];
                                        cntrs.value = val[1];
                                        result.Add(cntrs);
                                    }
                                    catch (Exception ex)
                                    {
                                        logger.LogWarning($"Не удалось отпарсить pctOfMaxConcurrentCalls {cntr.value} - {ex.Message}");
                                    }

                                }
                            }
                            break;
                        case "Message":
                            if (mon.name == "SMB_Share_List")
                                try
                                {
                                    metric.value = cntr.value.Split(' ')[0];
                                }
                                catch (Exception ex)
                                {
                                    logger.LogWarning($"Не удалось отпарсить SMB_Share_List Message {cntr.value} - {ex.Message}");
                                }
                            break;
                        case "Good Datastores":
                        case "Critical Datastores":
                        case "Warning Datastores":
                            if (mon.name == "VMware_DataStore_FreeSpace")
                            {
                                if (cntr.value == "n/a")
                                    continue; //Такова специфика монитора, что он по разным метрикам распихивает всё это счастье. И если тут n/a - значит просто датастор в другом состоянии, а не в текущем (По имени метрики)
                                var splitted = cntr.value.Split(new string[] { ", " }, StringSplitOptions.RemoveEmptyEntries);
                                foreach (var elt in splitted)
                                {
                                    try
                                    {
                                        var arr = elt.Split('=');
                                        var cntrs = (MuxLibrary.Data.MetricFull)monData.Clone();
                                        cntrs.MetricName = arr[0];
                                        cntrs.value = arr[1].Replace("%", "");
                                        result.Add(cntrs);
                                    }
                                    catch (Exception ex)
                                    {
                                        logger.LogWarning($"Не удалось отпарсить {cntr.name}, {cntr.value}, {ex.Message}");
                                    }
                                }
                            }
                            break;
                    }
                    result.Add(metric);
                }
                catch (Exception ex)
                {
                    logger.LogWarning($"Общая ошибка при обработке {cntr.name}, {cntr.value}, {ex.Message}\r\n{JsonConvert.SerializeObject(mon)}");
                }
                
            }
            return result;
        }

        
        
    }
}
