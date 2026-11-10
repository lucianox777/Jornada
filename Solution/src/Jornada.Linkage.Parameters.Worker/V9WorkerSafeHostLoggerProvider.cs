using Microsoft.Extensions.Logging;

namespace Jornada.Linkage.Parameters.Worker;

/// <summary>
/// Host and worker Warning/Error logs can carry a raw exception even when
/// Host.RunAsync handles it internally. This provider is the only recipient
/// for warning/error levels in Program.cs; it writes fixed codes only.
/// </summary>
public sealed class V9WorkerSafeHostLoggerProvider : ILoggerProvider
{
    private readonly TextWriter? publicError;
    private readonly Action? onFailure;

    public V9WorkerSafeHostLoggerProvider(TextWriter? publicError = null,
        Action? onFailure = null)
    {
        this.publicError = publicError;
        this.onFailure = onFailure;
    }

    public static bool AllowProviderLog(string? providerName, LogLevel level) =>
        level < LogLevel.Warning ||
        providerName?.Contains(nameof(V9WorkerSafeHostLoggerProvider), StringComparison.Ordinal) == true;

    public ILogger CreateLogger(string categoryName) => new SafeLogger(publicError, onFailure);

    public void Dispose() { }

    private sealed class SafeLogger(TextWriter? publicError, Action? onFailure) : ILogger
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => EmptyScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel < LogLevel.Warning) return;
            // Never call formatter: it can contain exception.Message, a SQL
            // connection string, CPF or other personal/secret information.
            var code = exception is null
                ? logLevel >= LogLevel.Error ? "V9_WORKER_FAILED:HOST_LOG_ERROR"
                    : "V9_WORKER_NOTICE:HOST_WARNING"
                : V9CalibradorLastErrorLog.SafeErrorCode(exception);
            try
            {
                var destination = publicError ?? Console.Error;
                destination.WriteLine(code);
                if (V9WorkerDevelopmentDiagnostics.Enabled())
                {
                    // Local Development only: show the formatted event as well as
                    // the complete exception. Production/CI never invoke formatter.
                    try { destination.WriteLine(formatter(state, exception)); }
                    catch { /* Formatter failure must not alter host exit behavior. */ }
                    if (exception is not null)
                        V9WorkerDevelopmentDiagnostics.WriteException(exception, destination);
                }
            }
            catch { /* Log I/O failure cannot trigger the unhandled .NET dumper. */ }
            if (logLevel >= LogLevel.Error || exception is not null)
            {
                if (onFailure is not null) onFailure();
                else Environment.ExitCode = 1;
            }
        }
    }

    private sealed class EmptyScope : IDisposable
    {
        public static readonly EmptyScope Instance = new();
        public void Dispose() { }
    }
}
