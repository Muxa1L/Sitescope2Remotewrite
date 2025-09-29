using Sitescope2RemoteWrite.Helpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Prometheus;
using Sitescope2RemoteWrite.Storage;
using Sitescope2RemoteWrite.Processing;
using Sitescope2RemoteWrite.Queueing;

namespace Sitescope2RemoteWrite
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddControllers(options => options.InputFormatters.Insert(0, new XDocumentInputFormatter()));
            services.AddHttpClient();

            services.AddHostedService<RemoteWriteSender>();
            services.AddSingleton<IDebugQueue, DebugQueue>();
             services.AddSingleton<ITimeSeriesQueue, TimeSeriesQueue>();
            if (Configuration.GetSection("zabbix").Exists())
            {
                
                services.AddSingleton<IZabbixMetricQueue, ZabbixMetricQueue>();
                
                services.AddHostedService<ZabbixMetricProcessor>();
                switch (Configuration.GetValue<string>("zabbix:DbType"))
                {
                    case "postgresql":
                        services.AddSingleton<ILabelStorage, LabelStoragePostgreSQL>();
                        services.AddHostedService<ZabbixPullerPostgreSQL>();
                        //services.AddSingleton<ReplicationStateStoragePostgreSQL>();
                        //services.AddHostedService(sp => sp.GetRequiredService<ReplicationStateStoragePostgreSQL>());
                        break;
                    case "mysql":
                        services.AddSingleton<ILabelStorage, LabelStorageMySQL>();
                        services.AddHostedService<ZabbixPullerMySQL>();
                        services.AddSingleton<ReplicationStateStorageMySQL>();
                        services.AddHostedService(sp => sp.GetRequiredService<ReplicationStateStorageMySQL>());
                        break;
                }
               
            }
            else
            {
                services.AddHostedService<XmlProcessor>();
                services.AddHostedService<MonitorProcessor>();
                services.AddSingleton<IXmlTaskQueue, XmlTaskQueue>();
                services.AddSingleton<IMonitorQueue, MonitorQueue>();
                services.AddSingleton<ITimeSeriesQueue, TimeSeriesQueue>();
            }
            
            


            //.UseHttpClientMetrics();
            //httpClientBuilder.UseHttpClientMetrics();
            //services.AddApplicationInsightsTelemetry();
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            //app.UseHttpsRedirection();

            app.UseRouting();
            app.UseHttpMetrics();
            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapMetrics();
            });
        }
    }
}
