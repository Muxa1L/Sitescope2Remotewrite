using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Sitescope2RemoteWrite.Processing {
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

    public class XmlProcessor : BackgroundService {
        private readonly ILogger<XmlProcessor> _logger;
        private readonly ConcurrentQueue<XDocument> _queue;
        public IServiceProvider Services { get; }
        public XmlProcessor(IServiceProvider services, ILogger<XmlProcessor> logger){
            _queue = new ConcurrentQueue<XDocument>();
            _logger = logger;
            Services = services;
        }

        public void EnqueueTask (XDocument task){
            if (task != null){
                _queue.Enqueue(task);
            }
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            
            _logger.LogInformation (
                "Xml Processor started"
            );
            await DoWork(stoppingToken);
        }
        private async Task DoWork(CancellationToken stoppingToken)
        {
            using (var scope = Services.CreateScope()){
                var processService = scope.ServiceProvider.GetRequiredService<IXmlTaskQueue>();
            }
            while (!stoppingToken.IsCancellationRequested){
                await Task.Delay(10000);
            }
        }
    }
}