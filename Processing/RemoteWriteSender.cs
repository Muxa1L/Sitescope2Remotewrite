using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using Newtonsoft.Json;
using Sitescope2RemoteWrite.Helpers;
using Sitescope2RemoteWrite.PromPb;
using Sitescope2RemoteWrite.Queueing;
using Sitescope2RemoteWrite.Storage;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
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

    public class RemoteWriteSender : IHostedService, IDisposable
    {
        private readonly ILogger<RemoteWriteSender> _logger;
        private readonly IConfiguration _remoteWriteConfig;
        private readonly IHttpClientFactory _clientFactory;
        private readonly ITimeSeriesQueue _timeSeriesQueue;
        private readonly ConcurrentQueue<WriteRequest> resendRequests = new ConcurrentQueue<WriteRequest>();
        private Timer _timer;
        private SemaphoreSlim _semaphore; 
        private int inwait = 0;
        private string remoteWriteUrl = "";
        private int sendPeriod = 0;
        private int chunks;

        public RemoteWriteSender(ILogger<RemoteWriteSender> logger, IHttpClientFactory clientFactory, IConfiguration config, ITimeSeriesQueue timeSeriesQueue)
        {
            _clientFactory = clientFactory;
            _logger = logger;     
            _remoteWriteConfig = config.GetSection("RemoteWrite");
            remoteWriteUrl = _remoteWriteConfig.GetValue<string>("url");
            sendPeriod = _remoteWriteConfig.GetValue<int>("period", 1);
            var threads = _remoteWriteConfig.GetValue<int>("threads", 1);
            chunks = _remoteWriteConfig.GetValue<int>("chunks", 100);
            _semaphore = new SemaphoreSlim(threads, threads);
            _timeSeriesQueue = timeSeriesQueue;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("RemoteWrite sender started");
            _timer = new Timer(DoWorkAsync, null, TimeSpan.Zero, TimeSpan.FromSeconds(sendPeriod));
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("RemoteWrite sender is stopping");

            _timer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }

        private async void DoWorkAsync(object state)
        {
            if (_semaphore.Wait(100))
            {
                Interlocked.Increment(ref inwait);
                WriteRequest writeRequest = new WriteRequest();
                try
                {
                    var cancelToken = new CancellationTokenSource(TimeSpan.FromSeconds(sendPeriod)).Token;
                    int countTs = 0;
                    do
                    {
                        TimeSeries timeSerie = null;
                        try
                        {
                            timeSerie = await _timeSeriesQueue.DequeueAsync(cancelToken);
                        }
                        catch (Exception) { }
                        if (timeSerie != null)
                        {
                            writeRequest.AddTimeSerie(timeSerie);
                            countTs++;
                        }
                    }
                    while (!cancelToken.IsCancellationRequested && countTs <= chunks);
                    if (resendRequests.TryDequeue(out var toResend))
                    {
                        var client = _clientFactory.CreateClient();
                        //client.DefaultRequestHeaders.Clear();
                        using (var ms = new MemoryStream())
                        {
                            ProtoBuf.Serializer.Serialize(ms, toResend);
                            var serialized = ms.ToArray();
                            var compressed = IronSnappy.Snappy.Encode(serialized);
                            var request = new HttpRequestMessage(HttpMethod.Post, remoteWriteUrl);
                            request.Content = new ByteArrayContent(compressed);
                            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-protobuf");
                            request.Content.Headers.ContentEncoding.Clear();
                            request.Content.Headers.ContentEncoding.Add("snappy");
                            request.Headers.Add("X-Prometheus-Remote-Write-Version", "0.1.0");

                            var result = client.SendAsync(request);
                            result.Wait();
                        }
                    }

                    if (countTs > 0)
                    {
                        var client = _clientFactory.CreateClient();
                        //client.DefaultRequestHeaders.Clear();
                        using (var ms = new MemoryStream())
                        {
                            ProtoBuf.Serializer.Serialize(ms, writeRequest);
                            var serialized = ms.ToArray();
                            var compressed = IronSnappy.Snappy.Encode(serialized);
                            var request = new HttpRequestMessage(HttpMethod.Post, remoteWriteUrl);
                            request.Content = new ByteArrayContent(compressed);
                            request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-protobuf");
                            request.Content.Headers.ContentEncoding.Clear();
                            request.Content.Headers.ContentEncoding.Add("snappy");
                            request.Headers.Add("X-Prometheus-Remote-Write-Version", "0.1.0");
                            
                            var result = client.SendAsync(request);
                            result.Wait();
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error while sending over remoteWrite");
                    resendRequests.Enqueue(writeRequest);
                }
                finally
                {
                    Interlocked.Decrement(ref inwait);
                    _semaphore.Release();
                }
                
            }
            
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }

        
    }
}