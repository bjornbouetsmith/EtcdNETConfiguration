using dotnet_etcd.DependencyInjection;
using dotnet_etcd.interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;

namespace ROOT.Etcd.Configuration;

public static class EtcdConfigurationProviderExtensions
{
    public static IHostBuilder AddEtcdConfiguration(
        this IHostBuilder hostBuilder,
        Action<EtcdOptions>? configureSource = null,
        IEtcdClientFactory? etcdClientFactory = null,
        TimeProvider? timeProvider = null)
    {
        var etcdConfiguration = new EtcdConfiguration();

        var etcdOptions = new EtcdOptions();

        configureSource?.Invoke(etcdOptions);

        etcdOptions.Validate();

        var clientFactory = etcdClientFactory ?? new EtcdClientFactory();

        hostBuilder.ConfigureServices((_, services) =>
        {
            services.AddSingleton(etcdOptions);
            services.AddSingleton(timeProvider ?? TimeProvider.System);
            services.AddSingleton<IEtcdConfiguration>(etcdConfiguration);
            services.AddSingleton<IEtcdConfigurationUpdater>(etcdConfiguration);
            services.AddSingleton<IEtcdConfigurationLoader>(serviceProvider =>
                new EtcdConfigurationLoader(
                    serviceProvider.GetRequiredService<IEtcdClient>(),
                    etcdOptions,
                    serviceProvider.GetRequiredService<ILogger<EtcdConfigurationLoader>>(),
                    timeProvider: serviceProvider.GetRequiredService<TimeProvider>(),
                    etcdConfiguration.Configuration));
            services.AddHostedService<EtcdPoller>();

            services.AddEtcdClient(options => ConfigureEtcdClient(etcdOptions, options));
        });

        hostBuilder.ConfigureHostConfiguration(config =>
        {
            // This source loads the initial values synchronously and seeds the shared
            // EtcdConfiguration used by both IConfiguration and the background poller.
            config.Add(new EtcdConfigurationSource(etcdOptions, etcdConfiguration, clientFactory));
        });

        return hostBuilder;
    }

    internal static void ConfigureEtcdClient(EtcdOptions etcdOptions, EtcdClientOptions clientOptions)
    {
        clientOptions.ConnectionString = etcdOptions.EndPoints;
        clientOptions.UseInsecureChannel = etcdOptions.UseInsecureChannels;
    }
}
