using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ROOT.Etcd.Configuration;

namespace ROOT.Etcd.ConfigurationTest;

public class EtcdConfigurationUnitTest
{
    [Fact]
    public void Configuration_WhenNothingHasBeenPublished_IsEmpty()
    {
        var configuration = new EtcdConfiguration();

        Assert.Empty(configuration.Configuration);
    }

    [Fact]
    public void PublishConfiguration_ReplacesTheCurrentConfiguration()
    {
        var configuration = new EtcdConfiguration();

        configuration.PublishConfiguration(new Dictionary<string, string?>
        {
            ["/configuration:one"] = "value-one",
            ["/configuration:two"] = "value-two"
        });

        Assert.Equal(2, configuration.Configuration.Count);
        Assert.Equal("value-one", configuration.Configuration["/configuration:one"]);
        Assert.Equal("value-two", configuration.Configuration["/configuration:two"]);
    }

    [Fact]
    public void PublishConfiguration_RemovesKeysThatAreNotInTheNewConfiguration()
    {
        var configuration = new EtcdConfiguration();
        configuration.PublishConfiguration(new Dictionary<string, string?>
        {
            ["/configuration:one"] = "value-one",
            ["/configuration:two"] = "value-two"
        });

        configuration.PublishConfiguration(new Dictionary<string, string?>
        {
            ["/configuration:one"] = "new-value"
        });

        var value = Assert.Single(configuration.Configuration);
        Assert.Equal("/configuration:one", value.Key);
        Assert.Equal("new-value", value.Value);
    }

    [Fact]
    public void PublishConfiguration_PreservesNullValues()
    {
        var configuration = new EtcdConfiguration();

        configuration.PublishConfiguration(new Dictionary<string, string?>
        {
            ["/configuration:empty"] = null
        });

        Assert.True(configuration.Configuration.ContainsKey("/configuration:empty"));
        Assert.Null(configuration.Configuration["/configuration:empty"]);
    }

    [Fact]
    public void PublishConfiguration_CopiesTheInputDictionary()
    {
        var configuration = new EtcdConfiguration();
        var data = new Dictionary<string, string?>
        {
            ["/configuration:one"] = "value-one"
        };

        configuration.PublishConfiguration(data);
        data["/configuration:one"] = "changed";
        data["/configuration:two"] = "value-two";

        Assert.Single(configuration.Configuration);
        Assert.Equal("value-one", configuration.Configuration["/configuration:one"]);
    }

    [Fact]
    public void PublishConfiguration_RaisesConfigurationChangedWithTheNewConfiguration()
    {
        var configuration = new EtcdConfiguration();
        IReadOnlyDictionary<string, string?>? changes = null;
        configuration.ConfigurationChanged += received => changes = received;

        configuration.PublishConfiguration(new Dictionary<string, string?>
        {
            ["/configuration:one"] = "value-one"
        });

        Assert.NotNull(changes);
        Assert.Single(changes);
        Assert.Equal("value-one", changes["/configuration:one"]);
    }

    [Fact]
    public void PublishConfiguration_UpdatesConfigurationBeforeRaisingTheEvent()
    {
        var configuration = new EtcdConfiguration();
        IReadOnlyDictionary<string, string?>? configurationDuringEvent = null;
        configuration.ConfigurationChanged += _ => configurationDuringEvent = configuration.Configuration;

        configuration.PublishConfiguration(new Dictionary<string, string?>
        {
            ["/configuration:one"] = "value-one"
        });

        Assert.NotNull(configurationDuringEvent);
        Assert.Equal("value-one", configurationDuringEvent["/configuration:one"]);
    }

    [Fact]
    public void PublishConfiguration_RaisesConfigurationChangedForEveryPublish()
    {
        var configuration = new EtcdConfiguration();
        var eventCount = 0;
        configuration.ConfigurationChanged += _ => eventCount++;

        configuration.PublishConfiguration(new Dictionary<string, string?>());
        configuration.PublishConfiguration(new Dictionary<string, string?>());

        Assert.Equal(2, eventCount);
    }

    [Fact]
    public async Task Configuration_CanBeReadConsistentlyWhilePublishing()
    {
        var configuration = new EtcdConfiguration();
        configuration.PublishConfiguration(new Dictionary<string, string?>
        {
            ["/configuration:one"] = "0",
            ["/configuration:two"] = "value-two"
        });

        var done = 0;
        var reads = 0;
        var firstRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = Task.Run(async () =>
        {
            while (Volatile.Read(ref done) == 0)
            {
                var snapshot = configuration.Configuration;
                Assert.True(snapshot.TryGetValue("/configuration:one", out var one));
                Assert.NotNull(one);
                Assert.Equal("value-two", snapshot["/configuration:two"]);
                reads++;
                firstRead.TrySetResult();
                await Task.Yield();
            }
        }, TestContext.Current.CancellationToken);

        await firstRead.Task;

        var writer = Task.Run(() =>
        {
            for (var i = 1; i <= 2_000; i++)
            {
                configuration.PublishConfiguration(new Dictionary<string, string?>
                {
                    ["/configuration:one"] = i.ToString(),
                    ["/configuration:two"] = "value-two"
                });
            }

            Volatile.Write(ref done, 1);
        }, TestContext.Current.CancellationToken);

        await Task.WhenAll(writer, reader);

        Assert.True(reads > 0);
        Assert.Equal("2000", configuration.Configuration["/configuration:one"]);
    }

    [Fact]
    public void PublishConfiguration_WithoutSubscribers_DoesNotThrow()
    {
        var configuration = new EtcdConfiguration();

        var exception = Record.Exception(() =>
            configuration.PublishConfiguration(new Dictionary<string, string?>
            {
                ["/configuration:one"] = "value-one"
            }));

        Assert.Null(exception);
    }
}
