using System.Collections.Generic;
using System.Threading;

namespace ROOT.Etcd.Configuration;

public delegate void ConfigurationChangedDelegate(IReadOnlyDictionary<string, string?> changes);

public interface IEtcdConfiguration
{
    event ConfigurationChangedDelegate ConfigurationChanged;
    IReadOnlyDictionary<string, string?> Configuration { get; }
}

public interface IEtcdConfigurationUpdater
{
    void PublishConfiguration(IReadOnlyDictionary<string, string?> configurationData);
}

public class EtcdConfiguration : IEtcdConfiguration, IEtcdConfigurationUpdater
{
    private Dictionary<string, string?>? _currentValues;
    
    public event ConfigurationChangedDelegate? ConfigurationChanged;

    public IReadOnlyDictionary<string, string?> Configuration =>
        Volatile.Read(ref _currentValues) ?? [];

    public EtcdConfiguration()
    {
    }

    internal EtcdConfiguration(IReadOnlyDictionary<string, string?> initialValues) 
    {
        _currentValues = new Dictionary<string,string?>(initialValues);
    }

    public void PublishConfiguration(IReadOnlyDictionary<string, string?> configurationData)
    {
        var newData = new Dictionary<string, string?>();
        foreach (var change in configurationData)
        {
            newData[change.Key] = change.Value;
        }

        Interlocked.Exchange(ref _currentValues, newData);
        var handler = ConfigurationChanged;
        handler?.Invoke(newData);
    }
}