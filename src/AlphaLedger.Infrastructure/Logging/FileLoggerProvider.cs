using Microsoft.Extensions.Logging;

namespace AlphaLedger.Infrastructure.Logging;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _directory;
    private readonly object _sync = new();

    public FileLoggerProvider(string directory)
    {
        _directory = directory;
        Directory.CreateDirectory(directory);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, _directory, _sync);

    public void Dispose()
    {
    }

    private sealed class FileLogger : ILogger
    {
        private readonly string _category;
        private readonly string _directory;
        private readonly object _sync;

        public FileLogger(string category, string directory, object sync)
        {
            _category = category;
            _directory = directory;
            _sync = sync;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var message = formatter(state, exception);
            if (LooksSensitive(message))
            {
                message = "[تم إخفاء محتوى قد يكون حساساً]";
            }

            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{logLevel}] {_category}: {message}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception.GetType().Name + ": " + exception.Message;
            }

            var file = Path.Combine(_directory, $"alpha-ledger-{DateTime.Now:yyyyMMdd}.log");
            lock (_sync)
            {
                File.AppendAllText(file, line + Environment.NewLine);
            }
        }

        private static bool LooksSensitive(string message)
        {
            var lower = message.ToLowerInvariant();
            return lower.Contains("password") || lower.Contains("كلمة السر") || lower.Contains("كلمة المرور");
        }
    }
}
