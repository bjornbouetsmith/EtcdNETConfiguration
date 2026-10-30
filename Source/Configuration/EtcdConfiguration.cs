using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ROOT.Etcd.Configuration;

public delegate Task ConfigurationChangedDelegate(IReadOnlyDictionary<string, string?> changes);

public interface IEtcdConfiguration
{
    event ConfigurationChangedDelegate ConfigurationChanged;
    IReadOnlyDictionary<string, string?> Configuration { get; }
}

public interface IEtcdConfigurationUpdater
{
    void PublishChanges(IReadOnlyDictionary<string, string?> changes);
}

public class EtcdConfiguration : IEtcdConfiguration, IEtcdConfigurationUpdater
{
    private Dictionary<string, string?>? _currentValues;
    private Dictionary<string, string?>? _previousValues;

    public event ConfigurationChangedDelegate? ConfigurationChanged;

    public IReadOnlyDictionary<string, string?> Configuration => _currentValues ?? [];

    public EtcdConfiguration()
    {
    }

    internal EtcdConfiguration(IReadOnlyDictionary<string, string?> initialValues) 
    {
        _currentValues = new Dictionary<string,string?>(initialValues);
    }

    public void PublishChanges(IReadOnlyDictionary<string, string?> changes)
    {
        var newData = new Dictionary<string, string>();
        foreach (var change in changes)
        {
            newData[change.Key] = change.Value;
        }
        Interlocked.Exchange(ref _previousValues, null);
        Interlocked.Exchange(ref _currentValues, newData);
    }
}