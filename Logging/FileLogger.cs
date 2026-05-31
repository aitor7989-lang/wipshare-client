using System.IO;
using Microsoft.Extensions.Logging;

namespace WipShare.Client.Logging;

public sealed class FileLoggerProvider : ILoggerProvider
{
    private readonly string _filePath;
    private readonly object _writeLock = new();
    private bool _disposed;

    public FileLoggerProvider(string filePath)
    {
        _filePath = filePath;
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(categoryName, this);

    internal void Write(string line)
    {
        if (_disposed) return;
        try
        {
            lock (_writeLock)
            {
                File.AppendAllText(_filePath, line + Environment.NewLine);
            }
        }
        catch (Exception ex)
        {
            // Logger must never throw; route the failure to the debugger so it stays visible.
            System.Diagnostics.Debug.WriteLine($"[FileLogger] write failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public void Dispose() => _disposed = true;
}

internal sealed class FileLogger : ILogger
{
    private static readonly IDisposable NullScope = new NoopScope();
    private readonly string _category;
    private readonly FileLoggerProvider _provider;

    public FileLogger(string category, FileLoggerProvider provider)
    {
        _category = category;
        _provider = provider;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Information;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;

        var msg = formatter(state, exception);
        var line = $"{DateTime.Now:yyyy-MM-ddTHH:mm:ss.fff} [{Level(logLevel)}] {_category}: {msg}";
        if (exception != null)
            line += Environment.NewLine + exception;
        _provider.Write(line);
    }

    private static string Level(LogLevel l) => l switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Information => "INF",
        LogLevel.Warning => "WRN",
        LogLevel.Error => "ERR",
        LogLevel.Critical => "CRT",
        _ => "???",
    };

    private sealed class NoopScope : IDisposable { public void Dispose() { } }
}
