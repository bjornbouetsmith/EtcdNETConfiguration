using dotnet_etcd.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;

namespace ROOT.Etcd.Configuration;

public static class EtcdConfigurationProviderExtensions
{
    public static IHostBuilder AddEtcdConfiguration(this IHostBuilder hostBuilder, Action<EtcdOptions> configureSource)
    {
        var etcdConfiguration = new EtcdConfiguration();
        
        var etcdOptions = new EtcdOptions();
        configureSource(etcdOptions);
                                       

        hostBuilder.ConfigureServices((_, services) => 
        {
            services.AddSingleton(etcdOptions);
            services.AddSingleton<IEtcdConfiguration>(etcdConfiguration);
            services.AddSingleton<IEtcdConfigurationUpdater>(etcdConfiguration);
            services.AddSingleton<IEtcdConfigurationLoader, EtcdConfigurationLoader>();
            services.AddHostedService<EtcdPoller>();

            services.AddEtcdClient(options => 
            {
                options.ConnectionString = etcdOptions.EndPoints;
                options.UseInsecureChannel = true;
            });
        });

        hostBuilder.ConfigureHostConfiguration(config =>
        {
            config.Add(new OneTimeEtcdConfiguration(etcdOptions));
            config.Add(new EtcdConfigurationSource(etcdConfiguration));
        });

        return hostBuilder;
    }
}