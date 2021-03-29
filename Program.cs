using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sitescope2RemoteWrite.Processing;
using Sitescope2RemoteWrite.Queueing;
using Sitescope2RemoteWrite.Storage;

namespace Sitescope2RemoteWrite
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>();
                })
                .ConfigureServices(services =>
                {
                    services.AddSingleton<ILabelStorage, LabelStorage>();
                    services.AddHostedService<XmlProcessor>();
                    services.AddHostedService<MonitorProcessor>();
                    services.AddHostedService<RemoteWriteSender>();
                    services.AddHostedService<ZabbixPuller>();
                    services.AddHostedService<ZabbixMetricProcessor>();

                    services.AddSingleton<IXmlTaskQueue, XmlTaskQueue>();
                    services.AddSingleton<IZabbixMetricQueue, ZabbixMetricQueue>();
                    services.AddSingleton<IMonitorQueue, MonitorQueue>();
                    services.AddSingleton<ITimeSeriesQueue, TimeSeriesQueue>();
                    services.AddSingleton<IDebugQueue, DebugQueue>();
                    

                    services.AddSingleton<ReplicationStateStorage>();
                    services.AddHostedService(sp => sp.GetRequiredService<ReplicationStateStorage>());
                });
    }
}
