using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using dotnet_etcd.DependencyInjection;
using dotnet_etcd.interfaces;
using Etcdserverpb;
using Grpc.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using ROOT.Etcd.Configuration;

namespace ROOT.Etcd.ConfigurationTest;

public class EtcdConfigurationProviderExtensionsTest
{
    [Fact]
    public void AddEtcdConfiguration_ReturnsTheSameHostBuilder()
    {
        var builder = new CapturingHostBuilder();

        var result = builder.AddEtcdConfiguration(_ => { });

        Assert.Same(builder, result);
    }

    [Fact]
    public void AddEtcdConfiguration_InvokesTheConfigureSourceActionOnce()
    {
        var builder = new CapturingHostBuilder();
        var callCount = 0;
        EtcdOptions? configuredOptions = null;

        builder.AddEtcdConfiguration(options =>
        {
            callCount++;
            options.EndPoints = "http://localhost:2379";
            configuredOptions = options;
        });

        Assert.Equal(1, callCount);
        Assert.NotNull(configuredOptions);
        Assert.Equal("http://localhost:2379", configuredOptions.EndPoints);
    }

    [Fact]
    public void AddEtcdConfiguration_RegistersTheConfigurationServices()
    {
        var builder = new CapturingHostBuilder();
        EtcdOptions? configuredOptions = null;
        builder.AddEtcdConfiguration(options =>
        {
            options.EndPoints = "https://localhost:2379";
            options.SslOptions = new SslOptions
            {
                UseSsl = true,
                ServerCertificateAuthorityPath = TestCertificates.CaCertificatePath
            };
            configuredOptions = options;
        });

        var services = new ServiceCollection();
        builder.ConfigureServicesAction!(
            new HostBuilderContext(new Dictionary<object, object>()),
            services);

        var optionsDescriptor = Assert.Single(
            services,
            descriptor => descriptor.ServiceType == typeof(EtcdOptions));
        Assert.NotNull(optionsDescriptor.ImplementationFactory);

        using (var provider = services.BuildServiceProvider())
        {
            var resolvedOptions = provider.GetRequiredService<EtcdOptions>();
            Assert.Equal(configuredOptions!.EndPoints, resolvedOptions.EndPoints);
            Assert.Equal(configuredOptions.RootKey, resolvedOptions.RootKey);
            Assert.Equal(configuredOptions.RefreshInterval, resolvedOptions.RefreshInterval);
            Assert.Equal(configuredOptions.ConfigurationTimeout, resolvedOptions.ConfigurationTimeout);
            Assert.Equal(configuredOptions.RedactConfigurationValues, resolvedOptions.RedactConfigurationValues);
            Assert.Equal(configuredOptions.SslOptions, resolvedOptions.SslOptions);
        }

        var timeProviderDescriptor = Assert.Single(
            services,
            descriptor => descriptor.ServiceType == typeof(TimeProvider));
        Assert.Same(TimeProvider.System, timeProviderDescriptor.ImplementationInstance);

        var configurationDescriptor = Assert.Single(
            services,
            descriptor => descriptor.ServiceType == typeof(IEtcdConfiguration));
        var updaterDescriptor = Assert.Single(
            services,
            descriptor => descriptor.ServiceType == typeof(IEtcdConfigurationUpdater));
        Assert.IsType<EtcdConfiguration>(configurationDescriptor.ImplementationInstance);
        Assert.Same(configurationDescriptor.ImplementationInstance, updaterDescriptor.ImplementationInstance);

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IEtcdConfigurationLoader));
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IHostedService) &&
                          descriptor.ImplementationFactory is not null);
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IEtcdPollerStatus));
    }

    [Fact]
    public void AddEtcdConfiguration_RegistersTheProvidedTimeProvider()
    {
        var builder = new CapturingHostBuilder();
        var timeProvider = new TestTimeProvider(new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero));
        builder.AddEtcdConfiguration(_ => { }, timeProvider: timeProvider);

        var services = new ServiceCollection();
        builder.ConfigureServicesAction!(
            new HostBuilderContext(new Dictionary<object, object>()),
            services);

        var timeProviderDescriptor = Assert.Single(
            services,
            descriptor => descriptor.ServiceType == typeof(TimeProvider));
        Assert.Same(timeProvider, timeProviderDescriptor.ImplementationInstance);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConfigureEtcdClient_UsesTheConfiguredSslSetting(bool useSsl)
    {
        var etcdOptions = new EtcdOptions
        {
            EndPoints = useSsl ? "https://etcd:2379" : "http://etcd:2379",
            SslOptions = new SslOptions { UseSsl = useSsl }
        };
        var clientOptions = new EtcdClientOptions();

        EtcdConfigurationProviderExtensions.ConfigureEtcdClient(etcdOptions, clientOptions);

        Assert.Equal(etcdOptions.EndPoints, clientOptions.ConnectionString);
        Assert.Equal(!useSsl, clientOptions.UseInsecureChannel);
        Assert.NotNull(clientOptions.ConfigureChannel);

        var channelOptions = new Grpc.Net.Client.GrpcChannelOptions();
        clientOptions.ConfigureChannel!(channelOptions);

        Assert.Equal(
            useSsl ? Grpc.Core.ChannelCredentials.SecureSsl : Grpc.Core.ChannelCredentials.Insecure,
            channelOptions.Credentials);
    }

    [Fact]
    public void ConfigureEtcdClient_WhenSslOptionsAreNotProvided_UsesInsecureChannel()
    {
        var etcdOptions = new EtcdOptions { EndPoints = "http://etcd:2379" };
        var clientOptions = new EtcdClientOptions();

        EtcdConfigurationProviderExtensions.ConfigureEtcdClient(etcdOptions, clientOptions);

        Assert.True(clientOptions.UseInsecureChannel);
    }

    [Fact]
    public void ConfigureEtcdClient_WhenCustomCaIsProvided_ConfiguresTlsValidation()
    {
        var etcdOptions = new EtcdOptions
        {
            EndPoints = "https://etcd:2379",
            SslOptions = new SslOptions
            {
                UseSsl = true,
                ServerCertificateAuthorityPath = TestCertificates.CaCertificatePath
            }
        };
        var clientOptions = new EtcdClientOptions();

        EtcdConfigurationProviderExtensions.ConfigureEtcdClient(etcdOptions, clientOptions);

        Assert.NotNull(clientOptions.ConfigureChannel);
        Assert.False(clientOptions.UseInsecureChannel);
    }

    [Fact]
    public void AddEtcdConfiguration_AddsTheHostConfigurationSources()
    {
        var builder = new CapturingHostBuilder();
        builder.AddEtcdConfiguration(_ => { });

        // Both host-builder overloads add the source to application configuration.
        Assert.Null(builder.ConfigureHostConfigurationAction);
        Assert.NotNull(builder.ConfigureAppConfigurationAction);

        var configurationBuilder = new ConfigurationBuilder();
        builder.ConfigureAppConfigurationAction!(
            new HostBuilderContext(new Dictionary<object, object>()),
            configurationBuilder);

        // There must be only one etcd-backed provider. A separate one-time provider
        // would keep deleted keys alive as a fallback.
        var source = Assert.Single(configurationBuilder.Sources);
        Assert.IsType<EtcdConfigurationSource>(source);
    }

    [Fact]
    public void AddEtcdConfiguration_UsesTheProvidedFactoryForTheSourceAndTheDiClient()
    {
        var factory = new Mock<IEtcdClientFactory>();
        factory
            .Setup(f => f.CreateClient(It.IsAny<EtcdOptions>()))
            .Returns(() =>
            {
                var client = new Mock<IEtcdClient>();
                client
                    .Setup(c => c.GetRange(
                        It.IsAny<string>(),
                        It.IsAny<Metadata>(),
                        It.IsAny<DateTime?>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(new RangeResponse());
                return client.Object;
            });
        var builder = new CapturingHostBuilder();

        builder.AddEtcdConfiguration(_ => { }, etcdClientFactory: factory.Object);

        var configurationBuilder = new ConfigurationBuilder();
        builder.ConfigureAppConfigurationAction!(
            new HostBuilderContext(new Dictionary<object, object>()),
            configurationBuilder);
        configurationBuilder.Build();

        using var provider = BuildServiceProvider(builder);
        Assert.NotNull(provider.GetRequiredService<IEtcdClient>());

        factory.Verify(f => f.CreateClient(It.IsAny<EtcdOptions>()), Times.Exactly(2));
    }

    [Fact]
    public void ApplicationBuilder_AddEtcdConfiguration_ReturnsTheSameHostBuilder()
    {
        var builder = new CapturingHostApplicationBuilder();

        var result = builder.AddEtcdConfiguration(
            _ => { },
            etcdClientFactory: CreateEmptyClientFactory());

        Assert.Same(builder, result);
    }

    [Fact]
    public void ApplicationBuilder_AddEtcdConfiguration_InvokesTheConfigureSourceActionOnce()
    {
        var builder = new CapturingHostApplicationBuilder();
        var callCount = 0;
        EtcdOptions? configuredOptions = null;

        builder.AddEtcdConfiguration(
            options =>
            {
                callCount++;
                options.EndPoints = "http://localhost:2379";
                configuredOptions = options;
            },
            etcdClientFactory: CreateEmptyClientFactory());

        Assert.Equal(1, callCount);
        Assert.NotNull(configuredOptions);
        Assert.Equal("http://localhost:2379", configuredOptions.EndPoints);
    }

    [Fact]
    public void ApplicationBuilder_AddEtcdConfiguration_RegistersTheConfigurationServices()
    {
        var builder = new CapturingHostApplicationBuilder();
        EtcdOptions? configuredOptions = null;
        builder.AddEtcdConfiguration(
            options =>
            {
                options.EndPoints = "https://localhost:2379";
                options.SslOptions = new SslOptions
                {
                    UseSsl = true,
                    ServerCertificateAuthorityPath = TestCertificates.CaCertificatePath
                };
                configuredOptions = options;
            },
            etcdClientFactory: CreateEmptyClientFactory());

        var services = builder.Services;

        var optionsDescriptor = Assert.Single(
            services,
            descriptor => descriptor.ServiceType == typeof(EtcdOptions));
        Assert.NotNull(optionsDescriptor.ImplementationFactory);

        using (var provider = services.BuildServiceProvider())
        {
            var resolvedOptions = provider.GetRequiredService<EtcdOptions>();
            Assert.Equal(configuredOptions!.EndPoints, resolvedOptions.EndPoints);
            Assert.Equal(configuredOptions.RootKey, resolvedOptions.RootKey);
            Assert.Equal(configuredOptions.RefreshInterval, resolvedOptions.RefreshInterval);
            Assert.Equal(configuredOptions.ConfigurationTimeout, resolvedOptions.ConfigurationTimeout);
            Assert.Equal(configuredOptions.RedactConfigurationValues, resolvedOptions.RedactConfigurationValues);
            Assert.Equal(configuredOptions.SslOptions, resolvedOptions.SslOptions);
        }

        var timeProviderDescriptor = Assert.Single(
            services,
            descriptor => descriptor.ServiceType == typeof(TimeProvider));
        Assert.Same(TimeProvider.System, timeProviderDescriptor.ImplementationInstance);

        var configurationDescriptor = Assert.Single(
            services,
            descriptor => descriptor.ServiceType == typeof(IEtcdConfiguration));
        var updaterDescriptor = Assert.Single(
            services,
            descriptor => descriptor.ServiceType == typeof(IEtcdConfigurationUpdater));
        Assert.IsType<EtcdConfiguration>(configurationDescriptor.ImplementationInstance);
        Assert.Same(configurationDescriptor.ImplementationInstance, updaterDescriptor.ImplementationInstance);

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IEtcdConfigurationLoader));
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IHostedService) &&
                          descriptor.ImplementationFactory is not null);
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IEtcdPollerStatus));
        Assert.Contains(
            services,
            descriptor => descriptor.ServiceType == typeof(IValidateOptions<EtcdOptions>));
    }

    [Fact]
    public void ApplicationBuilder_AddEtcdConfiguration_ConfiguresClientFromValidatedOptionsSnapshot()
    {
        var builder = new CapturingHostApplicationBuilder();
        EtcdOptions? configuredOptions = null;

        builder.AddEtcdConfiguration(
            options =>
            {
                options.EndPoints = "https://localhost:2379";
                options.SslOptions = new SslOptions
                {
                    UseSsl = true,
                    ServerCertificateAuthorityPath = TestCertificates.CaCertificatePath
                };
                configuredOptions = options;
            },
            etcdClientFactory: CreateEmptyClientFactory());

        using var provider = builder.Services.BuildServiceProvider();
        var validatedOptions = provider.GetRequiredService<EtcdOptions>();
        Assert.True(validatedOptions.SslOptions!.UseSsl);

        // The DI client must use the validated snapshot, not the caller's mutable object.
        configuredOptions!.EndPoints = "http://localhost:2379";

        using var client = provider.GetRequiredService<IEtcdClient>();
        Assert.NotNull(client);
    }

    [Fact]
    public void ApplicationBuilder_AddEtcdConfiguration_WhenTlsIsConfiguredAndClientIsRegistered_Throws()
    {
        var builder = new CapturingHostApplicationBuilder();
        builder.Services.AddSingleton(Mock.Of<IEtcdClient>());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            builder.AddEtcdConfiguration(
                options =>
                {
                    options.EndPoints = "https://localhost:2379";
                    options.SslOptions = new SslOptions
                    {
                        UseSsl = true,
                        ServerCertificateAuthorityPath = TestCertificates.CaCertificatePath
                    };
                },
                etcdClientFactory: CreateEmptyClientFactory()));

        Assert.Contains("already registered", exception.Message);
    }

    [Fact]
    public void ApplicationBuilder_AddEtcdConfiguration_WhenClientIsRegisteredWithoutTls_DoesNotThrow()
    {
        var builder = new CapturingHostApplicationBuilder();
        var existingClient = Mock.Of<IEtcdClient>();
        builder.Services.AddSingleton(existingClient);

        builder.AddEtcdConfiguration(
            _ => { },
            etcdClientFactory: CreateEmptyClientFactory());

        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(IEtcdClient) &&
                          ReferenceEquals(descriptor.ImplementationInstance, existingClient));

        using var provider = builder.Services.BuildServiceProvider();
        Assert.Same(existingClient, provider.GetRequiredService<IEtcdClient>());
    }

    [Fact]
    public void AddEtcdConfiguration_WhenTlsIsConfiguredAndClientIsRegistered_Throws()
    {
        var builder = new CapturingHostBuilder();
        builder.AddEtcdConfiguration(options =>
        {
            options.EndPoints = "https://localhost:2379";
            options.SslOptions = new SslOptions
            {
                UseSsl = true,
                ServerCertificateAuthorityPath = TestCertificates.CaCertificatePath
            };
        });

        var services = new ServiceCollection();
        services.AddSingleton(Mock.Of<IEtcdClient>());

        Assert.Throws<InvalidOperationException>(() =>
            builder.ConfigureServicesAction!(
                new HostBuilderContext(new Dictionary<object, object>()),
                services));
    }

    [Fact]
    public void ApplicationBuilder_AddEtcdConfiguration_RegistersTheProvidedTimeProvider()
    {
        var builder = new CapturingHostApplicationBuilder();
        var timeProvider = new TestTimeProvider(new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero));
        builder.AddEtcdConfiguration(
            _ => { },
            etcdClientFactory: CreateEmptyClientFactory(),
            timeProvider: timeProvider);

        var timeProviderDescriptor = Assert.Single(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(TimeProvider));
        Assert.Same(timeProvider, timeProviderDescriptor.ImplementationInstance);
    }

    [Fact]
    public void ApplicationBuilder_AddEtcdConfiguration_AddsTheHostConfigurationSources()
    {
        var builder = new CapturingHostApplicationBuilder();
        builder.AddEtcdConfiguration(
            _ => { },
            etcdClientFactory: CreateEmptyClientFactory());

        // There must be only one etcd-backed provider. A separate one-time provider
        // would keep deleted keys alive as a fallback.
        var source = Assert.Single(
            builder.Configuration.Sources,
            source => source is EtcdConfigurationSource);
        Assert.IsType<EtcdConfigurationSource>(source);
    }

    [Fact]
    public void ApplicationBuilder_AddEtcdConfiguration_RegistersDataAnnotationValidation()
    {
        var builder = new CapturingHostApplicationBuilder();

        builder.AddEtcdConfiguration(
            options => options.RefreshInterval = TimeSpan.FromSeconds(-1),
            etcdClientFactory: CreateEmptyClientFactory());

        using var provider = builder.Services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<EtcdOptions>());
        Assert.Contains("RefreshInterval", exception.Message);
    }

    [Fact]
    public async Task ApplicationBuilder_AddEtcdConfiguration_ValidatesOptionsOnStart()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddEtcdConfiguration(
            options => options.RefreshInterval = TimeSpan.FromSeconds(-1),
            etcdClientFactory: CreateEmptyClientFactory());

        using var host = builder.Build();

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(
            () => host.StartAsync(TestContext.Current.CancellationToken));
        Assert.Contains("RefreshInterval", exception.Message);
    }

    private static ServiceProvider BuildServiceProvider(CapturingHostBuilder builder)
    {
        var services = new ServiceCollection();
        builder.ConfigureServicesAction!(
            new HostBuilderContext(new Dictionary<object, object>()),
            services);
        return services.BuildServiceProvider();
    }

    private static IEtcdClientFactory CreateEmptyClientFactory()
    {
        var client = new Mock<IEtcdClient>();
        client
            .Setup(c => c.GetRange(
                It.IsAny<string>(),
                It.IsAny<Metadata>(),
                It.IsAny<DateTime?>(),
                It.IsAny<CancellationToken>()))
            .Returns(new RangeResponse());

        var factory = new Mock<IEtcdClientFactory>();
        factory
            .Setup(f => f.CreateClient(It.IsAny<EtcdOptions>()))
            .Returns(client.Object);

        return factory.Object;
    }

    private sealed class CapturingHostApplicationBuilder : IHostApplicationBuilder
    {
        public IConfigurationManager Configuration { get; } = new ConfigurationManager();

        public IServiceCollection Services { get; } = new ServiceCollection();

        public IHostEnvironment Environment { get; } = Mock.Of<IHostEnvironment>();

        public ILoggingBuilder Logging { get; } = Mock.Of<ILoggingBuilder>();

        public IMetricsBuilder Metrics { get; } = Mock.Of<IMetricsBuilder>();

        public IDictionary<object, object> Properties { get; } = new Dictionary<object, object>();

        public void ConfigureContainer<TContainerBuilder>(
            IServiceProviderFactory<TContainerBuilder> factory,
            Action<TContainerBuilder>? configure = null) where TContainerBuilder : notnull
        {
        }
    }

    private sealed class CapturingHostBuilder : IHostBuilder
    {
        public IDictionary<object, object> Properties { get; } = new Dictionary<object, object>();

        public Action<HostBuilderContext, IServiceCollection>? ConfigureServicesAction { get; private set; }

        public Action<IConfigurationBuilder>? ConfigureHostConfigurationAction { get; private set; }

        public Action<HostBuilderContext, IConfigurationBuilder>? ConfigureAppConfigurationAction { get; private set; }

        public IHostBuilder ConfigureHostConfiguration(Action<IConfigurationBuilder> configureDelegate)
        {
            ConfigureHostConfigurationAction += configureDelegate;
            return this;
        }

        public IHostBuilder ConfigureAppConfiguration(
            Action<HostBuilderContext, IConfigurationBuilder> configureDelegate)
        {
            ConfigureAppConfigurationAction += configureDelegate;
            return this;
        }

        public IHostBuilder ConfigureServices(
            Action<HostBuilderContext, IServiceCollection> configureDelegate)
        {
            ConfigureServicesAction += configureDelegate;
            return this;
        }

        public IHostBuilder ConfigureContainer<TContainerBuilder>(
            Action<HostBuilderContext, TContainerBuilder> configureDelegate) => this;

        public IHostBuilder UseServiceProviderFactory<TContainerBuilder>(
            IServiceProviderFactory<TContainerBuilder> factory) where TContainerBuilder : notnull => this;

        public IHostBuilder UseServiceProviderFactory<TContainerBuilder>(
            Func<HostBuilderContext, IServiceProviderFactory<TContainerBuilder>> factory)
            where TContainerBuilder : notnull => this;

        public IHost Build() => throw new NotSupportedException();
    }
}
