using System;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace ROOT.Etcd.ConfigurationTest;

public sealed class XUnitLoggerProvider : ILoggerProvider
{
    private readonly ITestOutputHelper _testOutputHelper;

    public XUnitLoggerProvider(ITestOutputHelper testOutputHelper)
    {
        _testOutputHelper = testOutputHelper;
    }

    public void Dispose()
    {
        // Nothing to do here
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new XUnitLogger(categoryName, _testOutputHelper);
    }

    public class XUnitLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly ITestOutputHelper _testOutputHelper;

        public XUnitLogger(string categoryName, ITestOutputHelper testOutputHelper)
        {
            _categoryName = categoryName;
            _testOutputHelper = testOutputHelper;
        }

        IDisposable ILogger.BeginScope<TState>(TState state)
        {
            return NoScope.Instance;
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }
            
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var logMessage = formatter(state, exception);
            var finalMessage = $"{DateTime.UtcNow:O} {_categoryName} [{logLevel}] : {logMessage} {exception}";

            _testOutputHelper.WriteLine(finalMessage);
        }
            

        private sealed class NoScope : IDisposable 
        {
            public static NoScope Instance { get; } = new NoScope();
            public void Dispose()
            {
                // Nothing
            }
        }
    }
}