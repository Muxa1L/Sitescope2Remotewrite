using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using Sitescope2RemoteWrite.Helpers;
using Sitescope2RemoteWrite.PromPb;
using Sitescope2RemoteWrite.Queueing;
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
        private readonly ILogger<XmlProcessor> _logger;
        private readonly IConfiguration _remoteWriteConfig;
        private readonly IHttpClientFactory _clientFactory;
        private readonly ITimeSeriesQueue _timeSeriesQueue;
        private Timer _timer;
        private SemaphoreSlim _semaphore;
        private IServiceProvider Services { get; }
        private int inwait = 0;
        private string remoteWriteUrl = "";

        public RemoteWriteSender(IServiceProvider services, ILogger<XmlProcessor> logger, IHttpClientFactory clientFactory, IConfiguration config, ITimeSeriesQueue timeSeriesQueue)
        {
            _clientFactory = clientFactory;
            _logger = logger;
            _semaphore = new SemaphoreSlim(1, 1);
            Services = services;
            _remoteWriteConfig = config.GetSection("RemoteWrite");
            remoteWriteUrl = _remoteWriteConfig.GetValue<string>("url");
            _timeSeriesQueue = timeSeriesQueue;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("RemoteWrite sender started");
            _timer = new Timer(DoWork, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("RemoteWrite sender is stopping");

            _timer?.Change(Timeout.Infinite, 0);
            return Task.CompletedTask;
        }

        private void DoWork(object state)
        {
            ITimeSeriesQueue timeSeriesQueue;
            if (_semaphore.Wait(100))
            {
                Interlocked.Increment(ref inwait);
                try
                {
                    WriteRequest writeRequest = new WriteRequest();
                    TimeSeries timeSerie;
                    bool gotSomething = false;
                    do
                    {
                        timeSerie = _timeSeriesQueue.Dequeue();
                        if (timeSerie != null)
                        {
                            writeRequest.AddTimeSerie(timeSerie);
                            gotSomething = true;
                        }
                            
                    }
                    while (timeSerie != null);
                    if (gotSomething)
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
                }
                Interlocked.Decrement(ref inwait);
                _semaphore.Release();
            }
            
        }

        public void Dispose()
        {
            _timer?.Dispose();
        }

        
    }
}