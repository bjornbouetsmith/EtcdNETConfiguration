# Refresh and Polling

Configuration refresh is poll-based. The library periodically reads all keys under `RootKey`, compares the result with the previous snapshot, and publishes only when something changed.

## How the poller works

`EtcdPoller` is registered as a hosted service by `AddEtcdConfiguration`. When the host starts:

1. The initial configuration has already been loaded synchronously by `EtcdConfigurationSource`.
2. The poller starts a timer and runs a first poll immediately.
3. After each run, the timer is re-armed for `RefreshInterval`.
4. If the loader detects added, changed, or removed keys, the poller publishes the new snapshot.
5. Publishing raises `ConfigurationChanged`, and the configuration provider calls `OnReload()` so `IConfiguration` and `IOptions`-bound settings are updated.

If nothing changed, the poller does not publish or log a change.

## `RefreshInterval`

`RefreshInterval` controls how often the poller checks etcd. The default is one minute.

```csharp
options.RefreshInterval = TimeSpan.FromMinutes(5);
```

Supported range:

- **Minimum:** `TimeSpan.Zero` (`00:00:00`).
- **Maximum:** `1.00:00:00` (24 hours).
- Any positive value up to 24 hours is supported.

## Disable polling with `TimeSpan.Zero`

Setting `RefreshInterval` to `TimeSpan.Zero` disables background polling:

```csharp
options.RefreshInterval = TimeSpan.Zero;
```

The initial configuration is still loaded once at startup by the configuration source, but `EtcdPoller` does not create a timer and does not poll for changes afterward. It logs:

```text
ETCD polling is disabled because RefreshInterval is set to TimeSpan.Zero
```

Because polling is disabled, changes made in etcd after startup will not be reflected in the application until it is restarted.

## Change detection

`EtcdConfigurationLoader` keeps the previous snapshot and compares it with each new read:

- New keys are logged as added.
- Changed values are logged with the previous and new value.
- Missing keys are logged as removed.

When `RedactConfigurationValues` is `true` (the default), values are replaced with `***` in the logs.

## Timeouts and errors

`ConfigurationTimeout` limits individual etcd calls. The default is one second; the supported range is one second to 60 seconds. Polls are also cancelled when the host stops, so shutdown is not blocked by a slow etcd read.

A single poll is limited by `MaximumConfigurationKeyCount` (default 10,000) and `MaximumConfigurationSizeBytes` (default 8 MiB). A read that exceeds either limit fails instead of allocating an unbounded dictionary.

The poller distinguishes transient failures from permanent ones:

- **Transient** failures, such as an unavailable endpoint, a deadline, or a cancelled request, are logged as errors and polling continues.
- **Permanent** failures, such as `Unauthenticated`, `PermissionDenied`, invalid TLS material, or an invalid configuration, are also logged as errors. If `MaximumConsecutivePermanentFailures` (default 5) is reached, the poller logs a critical message and asks the host to stop. Set the option to `0` to keep polling and only expose the failure.

A successful poll resets the consecutive permanent-failure count. The last failure is always available through `IEtcdPollerStatus`:

```csharp
public class EtcdHealthCheck
{
    private readonly IEtcdPollerStatus _status;

    public EtcdHealthCheck(IEtcdPollerStatus status) => _status = status;

    public bool IsHealthy()
    {
        if (!_status.IsHealthy)
        {
            // Log or report _status.LastFailure and _status.LastFailureTime.
            return false;
        }

        return true;
    }
}
```

`IsHealthy` becomes `false` after a permanent failure and returns to `true` after the next successful poll. A transient failure is reported through `LastFailure` and `LastFailureTime` without making the poller unhealthy.

## Related pages

- [Configuration Options](Configuration-Options)
- [How It Works](How-It-Works)
- [Troubleshooting](Troubleshooting)
