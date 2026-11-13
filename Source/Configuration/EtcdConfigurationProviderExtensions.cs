using dotnet_etcd.DependencyInjection;
using dotnet_etcd.interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;

namespace ROOT.Etcd.Configuration;

/// <summary>
/// Extension methods for configuring etcd integration in ASP.NET Core applications.
/// </summary>
public static class EtcdConfigurationProviderExtensions
{
    /// <summary>
    /// Adds etcd-backed configuration support to an application host builder.
    /// Configures the service collection with etcd client, options, poller, and initial configuration loading.
    /// </summary>
    /// <param name="hostBuilder">The host builder to configure.</param>
    /// <param name="configureSource">Optional action to customize etcd options before validation (e.g., set endpoints or timeout).</param>
    /// <param name="etcdClientFactory">Optional client factory; defaults to <see cref="EtcdClientFactory"/> if not provided.</param>
    /// <param name="timeProvider">Optional time provider; uses system time if null.</param>
    /// <returns>The same host builder with etcd configuration added.</returns>
    public static IHostBuilder AddEtcdConfiguration(
        this IHostBuilder hostBuilder,
        Action<EtcdOptions>? configureSource = null,
        IEtcdClientFactory? etcdClientFactory = null,
        TimeProvider? timeProvider = null)
    {
        var etcdConfiguration = new EtcdConfiguration();

        var etcdOptions = new EtcdOptions();

        configureSource?.Invoke(etcdOptions);

        var clientFactory = etcdClientFactory ?? new EtcdClientFactory();

        hostBuilder.ConfigureServices((_, services) =>
        {
            AddEtcdOptions(services, etcdOptions);
            services.AddSingleton(timeProvider ?? TimeProvider.System);
            services.AddSingleton<IEtcdConfiguration>(etcdConfiguration);
            services.AddSingleton<IEtcdConfigurationUpdater>(etcdConfiguration);
            services.AddSingleton<IEtcdConfigurationLoader>(serviceProvider =>
                new EtcdConfigurationLoader(
                    serviceProvider.GetRequiredService<IEtcdClient>(),
                    serviceProvider.GetRequiredService<EtcdOptions>(),
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

    /// <summary>
    /// Adds etcd-backed configuration support to an application host builder.
    /// Configures the service collection with etcd client, options, poller, and initial configuration loading.
    /// </summary>
    /// <param name="hostBuilder">The host application builder to configure.</param>
    /// <param name="configureSource">Optional action to customize etcd options before validation (e.g., set endpoints or timeout).</param>
    /// <param name="etcdClientFactory">Optional client factory; defaults to <see cref="EtcdClientFactory"/> if not provided.</param>
    /// <param name="timeProvider">Optional time provider; uses system time if null.</param>
    /// <returns>The same host builder with etcd configuration added.</returns>
    public static IHostApplicationBuilder AddEtcdConfiguration(
        this IHostApplicationBuilder hostBuilder,
        Action<EtcdOptions>? configureSource = null,
        IEtcdClientFactory? etcdClientFactory = null,
        TimeProvider? timeProvider = null)
    {
        var etcdConfiguration = new EtcdConfiguration();

        var etcdOptions = new EtcdOptions();

        configureSource?.Invoke(etcdOptions);

        var clientFactory = etcdClientFactory ?? new EtcdClientFactory();

        AddEtcdOptions(hostBuilder.Services, etcdOptions);
        hostBuilder.Services.AddSingleton(timeProvider ?? TimeProvider.System);
        hostBuilder.Services.AddSingleton<IEtcdConfiguration>(etcdConfiguration);
        hostBuilder.Services.AddSingleton<IEtcdConfigurationUpdater>(etcdConfiguration);

        hostBuilder.Services.AddSingleton<IEtcdConfigurationLoader>(serviceProvider =>
            new EtcdConfigurationLoader(
                serviceProvider.GetRequiredService<IEtcdClient>(),
                serviceProvider.GetRequiredService<EtcdOptions>(),
                serviceProvider.GetRequiredService<ILogger<EtcdConfigurationLoader>>(),
                timeProvider: serviceProvider.GetRequiredService<TimeProvider>(),
                etcdConfiguration.Configuration));

        hostBuilder.Services.AddHostedService<EtcdPoller>();

        hostBuilder.Services.AddEtcdClient(options => ConfigureEtcdClient(etcdOptions, options));

        // This source loads the initial values synchronously and seeds the shared
        // EtcdConfiguration used by both IConfiguration and the background poller.
        hostBuilder.Configuration.Add(new EtcdConfigurationSource(etcdOptions, etcdConfiguration, clientFactory));

        return hostBuilder;
    }

    /// <summary>
    /// Registers the etcd options with DataAnnotations validation that runs when options are
    /// resolved and when the host starts. The validated options instance is used for dependency
    /// injection so consumers receive the same values that were validated.
    /// </summary>
    /// <param name="services">The service collection to configure.</param>
    /// <param name="etcdOptions">The options configured by the caller.</param>
    private static void AddEtcdOptions(IServiceCollection services, EtcdOptions etcdOptions)
    {
        services.AddOptions<EtcdOptions>()
            .Configure(options => CopyOptions(etcdOptions, options))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton(serviceProvider =>
            serviceProvider.GetRequiredService<IOptions<EtcdOptions>>().Value);
    }

    /// <summary>
    /// Copies the caller-provided option values into the options instance created by the
    /// Microsoft.Extensions.Options infrastructure.
    /// </summary>
    /// <param name="source">The options configured by the caller.</param>
    /// <param name="target">The options instance to populate.</param>
    private static void CopyOptions(EtcdOptions source, EtcdOptions target)
    {
        target.EndPoints = source.EndPoints;
        target.RootKey = source.RootKey;
        target.RefreshInterval = source.RefreshInterval;
        target.ConfigurationTimeout = source.ConfigurationTimeout;
        target.RedactConfigurationValues = source.RedactConfigurationValues;
        target.UseInsecureChannels = source.UseInsecureChannels;
    }

    /// <summary>
    /// Configures the gRPC etcd client with options from the provided configuration.
    /// Sets the connection string and determines whether to use insecure channels.
    /// </summary>
    /// <param name="etcdOptions">The etcd options containing endpoint and security settings.</param>
    /// <param name="clientOptions">The gRPC client options to configure with etcd-specific values.</param>
    internal static void ConfigureEtcdClient(EtcdOptions etcdOptions, EtcdClientOptions clientOptions)
    {
        clientOptions.ConnectionString = etcdOptions.EndPoints;
        clientOptions.UseInsecureChannel = etcdOptions.UseInsecureChannels;
    }
}
