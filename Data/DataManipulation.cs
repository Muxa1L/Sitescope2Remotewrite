using MuxLibrary.Data;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using MuxLibrary;
using Confluent.Kafka;
using Microsoft.Extensions.Logging;
using Avro.Generic;

namespace Microsoft.Extensions.DependencyInjection
{
    public class DataManipulation:IDataManipulation
    {
        private List<Thread> threads                     = new List<Thread>();
        private BlockingCollection<byte[]> queue      = new BlockingCollection<byte[]>();
        private CancellationTokenSource cancelSource     = new CancellationTokenSource();
        private CancellationToken cancelToken;
        string kafkaServers;
        ProducerConfig kafkaConfig;
        string transformTopic;
        private readonly ILogger logger;
        public DataManipulation(Configuration.IConfiguration config, ILogger<DataManipulation> _logger)
        {
            logger = _logger;
            kafkaServers = config["kafka:bootstrapservers"];
            transformTopic = config["kafka:transformTopic"];
            kafkaConfig = new ProducerConfig{
                BootstrapServers = kafkaServers,
                Acks = Acks.Leader,
                CompressionType = CompressionType.Gzip,
                ClientId = System.Net.Dns.GetHostName(),
                //MessageTimeoutMs = 1000
            };

            cancelToken = cancelSource.Token;
            for (int i = 1; i <= 4; i++)
            {
                Thread nthread = new Thread(WorkerAsync);
                nthread.Start();
                nthread.Name = i + " worker";
                threads.Add(nthread);
            }
        }
        public void AddXmlToQueue (XDocument xml)
        {
            if (xml != null){
                try { 
                    var source = xml.Element("performanceMonitors").Attribute("collectorHost").Value.ToLower();
                    var monitors = xml.Descendants("monitor");
                    //task.source = source;
                    //task.monitors = new List<Monitor>();
                    foreach (var monitor in monitors)
                    {
                        var mon = new MuxLibrary.Data.Monitor()
                        {
                            path = source + monitor.Path(),
                            type = monitor.Attribute("type").Value,
                            name = monitor.Attribute("name").Value,
                            target = monitor.Attribute("target").Value,
                            targetIP = monitor.Attribute("targetIP").Value,
                            time = long.Parse(monitor.Attribute("time").Value).JavaTimeStampToDateTime(),
                            sourceTemplateName = monitor.Attribute("sourceTemplateName") == null ? "" : monitor.Attribute("sourceTemplateName").Value
                        };
                        //Округляем секунды. до полуминуты.
                        var sec = mon.time.Second;
                        mon.time = mon.time.AddSeconds(-sec);
                        if (sec < 15)
                            sec = 0;
                        if (sec >= 15 && sec < 45)
                            sec = 30;
                        if (sec >= 45)
                            sec = 60;
                        mon.time = mon.time.AddSeconds(sec);
                        var counters = new List<MuxLibrary.Data.Counter>();
                        foreach (var metr in monitor.Elements())
                        {
                            var cntr = new MuxLibrary.Data.Counter
                            {
                                name = (string)metr.Attribute("name"),
                                value = (string)metr.Attribute("value")
                            };
                            counters.Add(cntr);

                        }
                        mon.Counters = counters;
                        
                        String jsonified = Newtonsoft.Json.JsonConvert.SerializeObject(mon, Newtonsoft.Json.Formatting.None);
                        byte[] monitorBytes = Encoding.UTF8.GetBytes(jsonified);
                        queue.Add(monitorBytes);
                    }
                    monitors = null;
                }
                
                catch (Exception ex)
                {
                    logger.LogCritical("Не обработать файл {0}\r\n{1}", ex.Message, xml.ToString());
                    
                }
            }
        }
        
        private async void WorkerAsync()
        {
            byte[] data;
            
            using (var producer = 
                new ProducerBuilder<Null, byte[]>(kafkaConfig)
                    .Build())
            /*using (var producer = 
                new ProducerBuilder<Null, string>(kafkaConfig)
                    
                    .Build())*/
            while (!cancelToken.IsCancellationRequested)
            {
                try
                {
                    data = queue.Take(cancelToken);
                }
                catch (OperationCanceledException)
                {
                    break; //Просто выходим из цикла. 
                }
                catch (Exception ex)
                {
                    logger.LogError($"Не удалось получить задачу {ex.Message}");
                    continue;
                }
                try { 
                    var message = new Message<Null, byte[]>();
                    message.Value = data;
                    await producer
                        .ProduceAsync(transformTopic, message)
                        .ContinueWith(report => {
                            if (report.IsFaulted){
                                logger.LogError(report.Exception.Message + "");
                                queue.Add(report.Result.Value);
                            }
                                
                            else
                                logger.LogInformation(report.Status + " " + report.Result.TopicPartitionOffset);
                        });
                    //producer.CommitTransaction();
                    //byte[] monitorBytes = Encoding.UTF8.GetBytes(jsonified);
                }
                
                catch (Exception ex)
                {
                    logger.LogCritical("Не удалось отправить задачу {0}\r\n{1}", ex.Message, data);
                }
            }
        }
        
        public void Dispose()
        {
            SpinWait.SpinUntil(() => { return (queue.Count == 0); });
            cancelSource.Cancel();
            SpinWait.SpinUntil(() => {
                bool result = true;
                foreach (var thread in threads)
                {
                    if (thread.IsAlive)
                        result = false; 
                }
                return result;
            });
            queue.Dispose();
            cancelSource.Dispose();
        }

    }
}
