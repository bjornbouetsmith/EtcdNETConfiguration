using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ROOT.Etcd.Configuration;

/// <summary>
/// Configuration source implementation that builds an IConfiguration provider from etcd configuration data.
/// Loads initial configuration values and seeds the shared EtcdConfiguration instance used by
/// both IConfiguration and the background poller for consistent updates.
/// </summary>
public class EtcdConfigurationSource : IConfigurationSource
{
    private readonly EtcdOptions _options;
    private readonly EtcdConfiguration _sharedConfiguration;
    private readonly IEtcdClientFactory _clientFactory;
    private readonly ILogger<EtcdConfigurationSource> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="EtcdConfigurationSource"/> class.
    /// </summary>
    /// <param name="options">The etcd connection and behavior options.</param>
    /// <param name="sharedConfiguration">Shared configuration instance to be seeded with initial values.</param>
    /// <param name="clientFactory">Factory for creating new etcd clients.</param>
    /// <param name="logger">Optional logger used when the initial load fails.</param>
    internal EtcdConfigurationSource(
        EtcdOptions options,
        EtcdConfiguration sharedConfiguration,
        IEtcdClientFactory clientFactory,
        ILogger<EtcdConfigurationSource>? logger = null)
    {
        _options = options;
        _sharedConfiguration = sharedConfiguration;
        _clientFactory = clientFactory;
        _logger = logger ?? NullLogger<EtcdConfigurationSource>.Instance;
    }

    /// <inheritdoc/>
    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        var configuration = LoadInitialConfiguration();

        // Seed the same instance that the poller updates so there is only one
        // etcd-backed provider in the configuration chain.
        _sharedConfiguration.PublishConfiguration(configuration.Configuration);
        return new EtcdConfigurationProvider(_sharedConfiguration);
    }

    private EtcdConfiguration LoadInitialConfiguration()
    {
        // Fail closed: the host must not continue with an empty configuration when
        // etcd cannot be reached or TLS/authentication fails. Previously the
        // exception was swallowed, which could silently start the application
        // without its configuration or hide a downgraded plaintext connection.
        // Validate before creating the client so a custom factory cannot bypass
        // the TLS checks either.
        try
        {
            if (!_options.RedactConfigurationValues)
            {
                _logger.LogWarning(
                    "RedactConfigurationValues is false. Configuration values that do not look like secrets may be written to logs.");
            }

            EtcdTlsOptionsValidator.ValidateAndThrow(_options);

            using var client = _clientFactory.CreateClient(_options);
                       
            EtcdConfigurationLoader loader = new EtcdConfigurationLoader(client, _options, NullLogger<EtcdConfigurationLoader>.Instance);
            var data = loader.LoadConfiguration();
            return new EtcdConfiguration(data.Configuration);
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "Failed to load the initial etcd configuration. The host will not start.");
            throw;
        }
    }
}
