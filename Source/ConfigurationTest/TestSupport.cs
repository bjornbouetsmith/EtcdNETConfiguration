using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace ROOT.Etcd.ConfigurationTest;

internal readonly record struct TestLogEntry(LogLevel Level, string Message);

internal sealed class TestLogger<T> : ILogger<T>
{
    private readonly ConcurrentQueue<TestLogEntry> _entries = new();

    public IReadOnlyList<TestLogEntry> Entries => [.. _entries];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        _entries.Enqueue(new TestLogEntry(logLevel, formatter(state, exception)));
    }
}

internal sealed class TestTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<TestTimer> _timers = [];
    private DateTimeOffset _utcNow;

    public TestTimeProvider(DateTimeOffset utcNow)
    {
        _utcNow = utcNow;
    }

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;

    public TestTimer Timer
    {
        get
        {
            lock (_gate)
            {
                return Assert.Single(_timers);
            }
        }
    }

    public override ITimer CreateTimer(
        TimerCallback callback,
        object? state,
        TimeSpan dueTime,
        TimeSpan period)
    {
        var timer = new TestTimer(callback, state, dueTime, period);
        lock (_gate)
        {
            _timers.Add(timer);
        }

        return timer;
    }

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _utcNow;
        }
    }

    public void Advance(TimeSpan delta)
    {
        lock (_gate)
        {
            _utcNow = _utcNow.Add(delta);
        }
    }
}

internal sealed class TestTimer : ITimer
{
    private readonly object _gate = new();
    private readonly TimerCallback _callback;
    private readonly object? _state;
    private TimeSpan _dueTime;
    private TimeSpan _period;

    public TestTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        _callback = callback;
        _state = state;
        _dueTime = dueTime;
        _period = period;
    }

    public TimeSpan DueTime
    {
        get
        {
            lock (_gate)
            {
                return _dueTime;
            }
        }
    }

    public TimeSpan Period
    {
        get
        {
            lock (_gate)
            {
                return _period;
            }
        }
    }

    public int ChangeCount { get; private set; }

    public bool IsDisposed { get; private set; }

    public void Fire()
    {
        TimerCallback callback;
        object? state;
        lock (_gate)
        {
            if (IsDisposed)
            {
                return;
            }

            callback = _callback;
            state = _state;
        }

        callback(state);
    }

    /// <summary>
    /// Simulates a callback that was already queued before the timer was disposed.
    /// </summary>
    public void FireIgnoringDisposal()
    {
        TimerCallback callback;
        object? state;
        lock (_gate)
        {
            callback = _callback;
            state = _state;
        }

        callback(state);
    }

    public bool Change(TimeSpan dueTime, TimeSpan period)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            _dueTime = dueTime;
            _period = period;
            ChangeCount++;
            return true;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            IsDisposed = true;
        }
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}

internal static class TestWait
{
    public static async Task UntilAsync(Func<bool> condition, string message, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException(message);
            }

            await Task.Delay(10);
        }
    }
}
