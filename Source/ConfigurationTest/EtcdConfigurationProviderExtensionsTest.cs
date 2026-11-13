using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using dotnet_etcd.DependencyInjection;
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
            options.EndPoints = "http://localhost:2379";
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
            Assert.Equal(configuredOptions.UseInsecureChannels, resolvedOptions.UseInsecureChannels);
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
                          descriptor.ImplementationType == typeof(EtcdPoller));
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
    public void ConfigureEtcdClient_UsesTheConfiguredInsecureChannelSetting(bool useInsecureChannels)
    {
        var etcdOptions = new EtcdOptions
        {
            EndPoints = "https://etcd:2379",
            UseInsecureChannels = useInsecureChannels
        };
        var clientOptions = new EtcdClientOptions();

        EtcdConfigurationProviderExtensions.ConfigureEtcdClient(etcdOptions, clientOptions);

        Assert.Equal(etcdOptions.EndPoints, clientOptions.ConnectionString);
        Assert.Equal(useInsecureChannels, clientOptions.UseInsecureChannel);
    }

    [Fact]
    public void AddEtcdConfiguration_AddsTheHostConfigurationSources()
    {
        var builder = new CapturingHostBuilder();
        builder.AddEtcdConfiguration(_ => { });

        var configurationBuilder = new ConfigurationBuilder();
        builder.ConfigureHostConfigurationAction!(configurationBuilder);

        // There must be only one etcd-backed provider. A separate one-time provider
        // would keep deleted keys alive as a fallback.
        var source = Assert.Single(configurationBuilder.Sources);
        Assert.IsType<EtcdConfigurationSource>(source);
    }

    [Fact]
    public void ApplicationBuilder_AddEtcdConfiguration_ReturnsTheSameHostBuilder()
    {
        var builder = new CapturingHostApplicationBuilder();

        var result = builder.AddEtcdConfiguration(_ => { });

        Assert.Same(builder, result);
    }

    [Fact]
    public void ApplicationBuilder_AddEtcdConfiguration_InvokesTheConfigureSourceActionOnce()
    {
        var builder = new CapturingHostApplicationBuilder();
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
    public void ApplicationBuilder_AddEtcdConfiguration_RegistersTheConfigurationServices()
    {
        var builder = new CapturingHostApplicationBuilder();
        EtcdOptions? configuredOptions = null;
        builder.AddEtcdConfiguration(options =>
        {
            options.EndPoints = "http://localhost:2379";
            configuredOptions = options;
        });

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
            Assert.Equal(configuredOptions.UseInsecureChannels, resolvedOptions.UseInsecureChannels);
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
                          descriptor.ImplementationType == typeof(EtcdPoller));
    }

    [Fact]
    public void ApplicationBuilder_AddEtcdConfiguration_RegistersTheProvidedTimeProvider()
    {
        var builder = new CapturingHostApplicationBuilder();
        var timeProvider = new TestTimeProvider(new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero));
        builder.AddEtcdConfiguration(_ => { }, timeProvider: timeProvider);

        var timeProviderDescriptor = Assert.Single(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(TimeProvider));
        Assert.Same(timeProvider, timeProviderDescriptor.ImplementationInstance);
    }

    [Fact]
    public void ApplicationBuilder_AddEtcdConfiguration_AddsTheHostConfigurationSources()
    {
        var builder = new CapturingHostApplicationBuilder();
        builder.AddEtcdConfiguration(_ => { });

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

        builder.AddEtcdConfiguration(options => options.RefreshInterval = TimeSpan.FromSeconds(-1));

        using var provider = builder.Services.BuildServiceProvider();

        var exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<EtcdOptions>());
        Assert.Contains("RefreshInterval", exception.Message);
    }

    [Fact]
    public async Task ApplicationBuilder_AddEtcdConfiguration_ValidatesOptionsOnStart()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddEtcdConfiguration(options => options.RefreshInterval = TimeSpan.FromSeconds(-1));

        using var host = builder.Build();

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(
            () => host.StartAsync(TestContext.Current.CancellationToken));
        Assert.Contains("RefreshInterval", exception.Message);
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

        public IHostBuilder ConfigureHostConfiguration(Action<IConfigurationBuilder> configureDelegate)
        {
            ConfigureHostConfigurationAction += configureDelegate;
            return this;
        }

        public IHostBuilder ConfigureAppConfiguration(
            Action<HostBuilderContext, IConfigurationBuilder> configureDelegate) => this;

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
