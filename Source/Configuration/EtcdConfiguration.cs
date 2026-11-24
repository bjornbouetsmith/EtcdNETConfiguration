using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;

namespace ROOT.Etcd.Configuration;

/// <summary>
/// Delegate invoked when configuration changes occur.
/// </summary>
/// <param name="changes">Read-only dictionary of changed or new key-value pairs.</param>
public delegate void ConfigurationChangedHandler(IReadOnlyDictionary<string, string?> changes);

/// <summary>
/// Interface representing an etcd-backed configuration source.
/// Provides access to the current configuration and change notifications.
/// </summary>
public interface IEtcdConfiguration
{
    /// <summary>
    /// Event raised when the underlying configuration data has changed.
    /// </summary>
    event ConfigurationChangedHandler ConfigurationChanged;

    /// <summary>
    /// Gets a read-only dictionary of current configuration key-value pairs.
    /// </summary>
    IReadOnlyDictionary<string, string?> Configuration { get; }
}

/// <summary>
/// Interface for publishing/updating configuration data in etcd-backed configuration system.
/// </summary>
public interface IEtcdConfigurationUpdater
{
    /// <summary>
    /// Publishes new configuration data that will be stored and propagate to subscribers.
    /// </summary>
    /// <param name="configurationData">Read-only dictionary of key-value pairs representing the new configuration.</param>
    void PublishConfiguration(IReadOnlyDictionary<string, string?> configurationData);
}

/// <summary>
/// Etcd-backed configuration model that tracks current values and notifies on changes.
/// Implements both IEtcdConfiguration for value retrieval and notifications,
/// and IEtcdConfigurationUpdater for publishing new configuration data.
/// </summary>
public class EtcdConfiguration : IEtcdConfiguration, IEtcdConfigurationUpdater
{
    private static readonly IReadOnlyDictionary<string, string?> EmptyConfiguration =
        new ReadOnlyDictionary<string, string?>(
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase));

    private IReadOnlyDictionary<string, string?>? _currentValues;
    
    /// <inheritdoc/>
    public event ConfigurationChangedHandler? ConfigurationChanged;

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, string?> Configuration =>
        Volatile.Read(ref _currentValues) ?? EmptyConfiguration;

    /// <summary>
    /// Initializes a new instance of the <see cref="EtcdConfiguration"/> class with no initial values.
    /// </summary>
    public EtcdConfiguration()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EtcdConfiguration"/> class with the provided initial configuration.
    /// </summary>
    /// <param name="initialValues">Read-only dictionary of initial key-value pairs for the configuration.</param>
    internal EtcdConfiguration(IReadOnlyDictionary<string, string?> initialValues) 
    {
        var copy = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in initialValues)
        {
            copy[kv.Key] = kv.Value;
        }

        _currentValues = new ReadOnlyDictionary<string, string?>(copy);
    }

    /// <inheritdoc/>
    public void PublishConfiguration(IReadOnlyDictionary<string, string?> configurationData)
    {
        var newData = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var change in configurationData)
        {
            newData[change.Key] = change.Value;
        }

        IReadOnlyDictionary<string, string?> publishedData =
            new ReadOnlyDictionary<string, string?>(newData);

        Interlocked.Exchange(ref _currentValues, publishedData);
        var handler = ConfigurationChanged;
        handler?.Invoke(publishedData);
    }
}