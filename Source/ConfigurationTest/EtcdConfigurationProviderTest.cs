using System.Collections.Generic;
using ROOT.Etcd.Configuration;

namespace ROOT.Etcd.ConfigurationTest;

public class EtcdConfigurationProviderTest
{
    [Fact]
    public void Load_CopiesConfigurationIntoData()
    {
        var configuration = new TestEtcdConfiguration
        {
            Configuration = new Dictionary<string, string?>
            {
                ["/configuration:one"] = "value-one",
                ["/configuration:empty"] = null
            }
        };
        var provider = new EtcdConfigurationProvider(configuration);

        provider.Load();

        Assert.True(provider.TryGet("/configuration:one", out var value));
        Assert.Equal("value-one", value);
        Assert.True(provider.TryGet("/configuration:empty", out var emptyValue));
        Assert.Null(emptyValue);
    }

    [Fact]
    public void Load_CopiesTheConfigurationInsteadOfKeepingAReference()
    {
        var data = new Dictionary<string, string?>
        {
            ["/configuration:one"] = "value-one"
        };
        var configuration = new TestEtcdConfiguration { Configuration = data };
        var provider = new EtcdConfigurationProvider(configuration);

        provider.Load();
        data["/configuration:one"] = "changed";
        data["/configuration:two"] = "value-two";

        Assert.True(provider.TryGet("/configuration:one", out var value));
        Assert.Equal("value-one", value);
        Assert.False(provider.TryGet("/configuration:two", out _));
    }

    [Fact]
    public void Load_RaisesTheReloadToken()
    {
        var provider = new EtcdConfigurationProvider(new TestEtcdConfiguration());
        var reloaded = false;
        provider.GetReloadToken().RegisterChangeCallback(_ => reloaded = true, null);

        provider.Load();

        Assert.True(reloaded);
    }

    [Fact]
    public void ConfigurationChanged_ReplacesData()
    {
        var configuration = new TestEtcdConfiguration();
        var provider = new EtcdConfigurationProvider(configuration);

        configuration.Publish(new Dictionary<string, string?>
        {
            ["/configuration:one"] = "value-one",
            ["/configuration:two"] = "value-two"
        });

        Assert.True(provider.TryGet("/configuration:one", out var first));
        Assert.Equal("value-one", first);
        Assert.True(provider.TryGet("/configuration:two", out var second));
        Assert.Equal("value-two", second);
    }

    [Fact]
    public void ConfigurationChanged_RemovesKeysThatAreNotInTheNewConfiguration()
    {
        var configuration = new TestEtcdConfiguration();
        var provider = new EtcdConfigurationProvider(configuration);
        configuration.Publish(new Dictionary<string, string?>
        {
            ["/configuration:one"] = "value-one",
            ["/configuration:two"] = "value-two"
        });

        configuration.Publish(new Dictionary<string, string?>
        {
            ["/configuration:one"] = "new-value"
        });

        Assert.True(provider.TryGet("/configuration:one", out var value));
        Assert.Equal("new-value", value);
        Assert.False(provider.TryGet("/configuration:two", out _));
    }

    [Fact]
    public void ConfigurationChanged_RaisesTheReloadToken()
    {
        var configuration = new TestEtcdConfiguration();
        var provider = new EtcdConfigurationProvider(configuration);
        var reloaded = false;
        provider.GetReloadToken().RegisterChangeCallback(_ => reloaded = true, null);

        configuration.Publish(new Dictionary<string, string?>
        {
            ["/configuration:one"] = "value-one"
        });

        Assert.True(reloaded);
    }

    [Fact]
    public void ConfigurationChanged_PreservesNullValues()
    {
        var configuration = new TestEtcdConfiguration();
        var provider = new EtcdConfigurationProvider(configuration);

        configuration.Publish(new Dictionary<string, string?>
        {
            ["/configuration:empty"] = null
        });

        Assert.True(provider.TryGet("/configuration:empty", out var value));
        Assert.Null(value);
    }

    private sealed class TestEtcdConfiguration : IEtcdConfiguration
    {
        public event ConfigurationChangedDelegate? ConfigurationChanged;

        public IReadOnlyDictionary<string, string?> Configuration { get; set; } =
            new Dictionary<string, string?>();

        public void Publish(IReadOnlyDictionary<string, string?> changes)
        {
            ConfigurationChanged?.Invoke(changes);
        }
    }
}
