using dotnet_etcd.DependencyInjection;
using dotnet_etcd.interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Linq;

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
            EnsureNoConflictingEtcdClientRegistration(services, etcdOptions);
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

            services.AddEtcdClient((serviceProvider, clientOptions) =>
                ConfigureEtcdClient(serviceProvider.GetRequiredService<EtcdOptions>(), clientOptions));
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

        EnsureNoConflictingEtcdClientRegistration(hostBuilder.Services, etcdOptions);
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

        hostBuilder.Services.AddEtcdClient((serviceProvider, clientOptions) =>
            ConfigureEtcdClient(serviceProvider.GetRequiredService<EtcdOptions>(), clientOptions));

        // This source loads the initial values synchronously and seeds the shared
        // EtcdConfiguration used by both IConfiguration and the background poller.
        hostBuilder.Configuration.Add(new EtcdConfigurationSource(etcdOptions, etcdConfiguration, clientFactory));

        return hostBuilder;
    }

    /// <summary>
    /// dotnet-etcd registers <see cref="IEtcdClient"/> with TryAddSingleton, so a pre-existing
    /// registration would silently win and the TLS-configured client would never be used.
    /// Fail instead of starting with a client that bypasses the configured TLS settings.
    /// </summary>
    private static void EnsureNoConflictingEtcdClientRegistration(
        IServiceCollection services,
        EtcdOptions etcdOptions)
    {
        if (etcdOptions.SslOptions?.UseSsl != true)
        {
            return;
        }

        if (services.Any(descriptor => descriptor.ServiceType == typeof(IEtcdClient)))
        {
            throw new InvalidOperationException(
                "An IEtcdClient is already registered. The TLS-configured etcd client from " +
                "AddEtcdConfiguration would be ignored because dotnet-etcd registers IEtcdClient " +
                "with TryAddSingleton. Register AddEtcdConfiguration before any other IEtcdClient " +
                "registration, or remove the conflicting registration.");
        }
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

        services.AddSingleton<IValidateOptions<EtcdOptions>, EtcdTlsOptionsValidator>();

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
        target.SslOptions = source.SslOptions is null ? null : source.SslOptions with { };
    }

    /// <summary>
    /// Configures the gRPC etcd client with options from the provided configuration.
    /// Sets the connection string and determines whether to use SSL.
    /// </summary>
    /// <param name="etcdOptions">The etcd options containing endpoint and security settings.</param>
    /// <param name="clientOptions">The gRPC client options to configure with etcd-specific values.</param>
    internal static void ConfigureEtcdClient(EtcdOptions etcdOptions, EtcdClientOptions clientOptions)
    {
        clientOptions.ConnectionString = etcdOptions.EndPoints;
        clientOptions.UseInsecureChannel = etcdOptions.SslOptions?.UseSsl != true;
        clientOptions.ConfigureChannel = channelOptions =>
            EtcdClientFactory.ConfigureChannel(etcdOptions, channelOptions);
    }
}
