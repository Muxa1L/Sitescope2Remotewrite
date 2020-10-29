using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sitescope2RemoteWrite.Processing;
using Sitescope2RemoteWrite.Queueing;

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
                    services.AddHostedService<XmlProcessor>();
                    services.AddHostedService<MonitorProcessor>();
                    services.AddSingleton<IXmlTaskQueue, XmlTaskQueue>();
                    services.AddSingleton<IMonitorQueue, MonitorQueue>();
                    services.AddSingleton<ITimeSeriesQueue, TimeSeriesQueue>();
                });
    }
}
