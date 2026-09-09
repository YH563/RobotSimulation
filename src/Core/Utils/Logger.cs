using System;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace RobotSimulation.Core.Utils;

/// <summary>
/// Process-wide internal static logger: a thin facade over <see cref="Microsoft.Extensions.Logging"/>,
/// so modules can use it directly without DI.
/// </summary>
/// <remarks>
/// Key contracts:
/// 1. If <see cref="Initialize"/> is not called before the first log, a default factory is created
///    automatically with no providers (only the <see cref="OnLog"/> event is available). A host that
///    wants console/file logging must call <see cref="Initialize"/> with a provider configuration at
///    the composition root, before any log output (initialization is idempotent afterward).
/// 2. When a configure delegate is passed, provider configuration is entirely owned by the caller
///    (no console provider is auto-attached).
/// 3. Level filtering is controlled by the <see cref="MinimumLevel"/> master switch (default Debug,
///    adjustable at runtime).
/// 4. Convenience methods always use the <see cref="DefaultCategory"/> category; use
///    <see cref="GetLogger{T}"/> for separate categories.
/// 5. Call <see cref="Shutdown"/> before process exit to dispose the LoggerFactory.
/// </remarks>
public static class Logger
{
    /// <summary>Default log category used by the convenience methods.</summary>
    public const string DefaultCategory = "Global";

    // Message written to the provider when an OnLog subscriber throws (goes straight to the provider to
    // avoid recursively triggering the event).
    private const string SubscriberFailureMessage = "An OnLog subscriber threw an exception; it was swallowed by the logger and execution continued (the subscriber should investigate).";

    // ---- Initialization state ----
    // double-checked locking; _factory/_logger are volatile for cross-thread visibility.
    private static readonly object SyncRoot = new();
    private static volatile ILoggerFactory? _factory;
    private static volatile ILogger _logger = NullLogger.Instance;

    // LogLevel is an int underneath; use Volatile.Read/Write for lock-free access, allowing runtime adjustment.
    private static int _minimumLevel = (int)LogLevel.Debug;

    /// <summary>Raised for every log that passes the master switch. If <see cref="UiContext"/> is set, the callback is marshaled to the UI thread.
    /// A subscriber throwing does not affect the caller (it is caught and logged once).</summary>
    public static event Action<LogLevel, string, Exception?>? OnLog;

    /// <summary>When set, <see cref="OnLog"/> callbacks are posted to this SynchronizationContext (usually the UI thread).</summary>
    public static SynchronizationContext? UiContext { get; set; }

    /// <summary>Runtime log-level master switch: levels below this are neither written to providers nor raise <see cref="OnLog"/>. Thread-safe.</summary>
    public static LogLevel MinimumLevel
    {
        get => (LogLevel)Volatile.Read(ref _minimumLevel);
        set => Volatile.Write(ref _minimumLevel, (int)value);
    }

    /// <summary>Queries whether the given level is currently logged (hot paths can avoid eagerly building strings).</summary>
    public static bool IsEnabled(LogLevel level) => level >= MinimumLevel;

    /// <summary>Explicit initialization (thread-safe, idempotent). With a null configure, a provider-less default factory is used; otherwise providers are fully owned by the caller.</summary>
    public static void Initialize(Action<ILoggingBuilder>? configure = null)
    {
        lock (SyncRoot)
        {
            if (_factory != null) return;
            _factory = BuildFactory(configure);
            _logger = _factory.CreateLogger(DefaultCategory);
        }
    }

    /// <summary>Disposes the internal LoggerFactory (flushing provider write queues); <see cref="Initialize"/> may be called again. Call before Host exit.</summary>
    public static void Shutdown()
    {
        lock (SyncRoot)
        {
            _logger = NullLogger.Instance;
            var factory = _factory;
            _factory = null;
            factory?.Dispose();
        }
    }

    // ---- Convenience methods (category fixed to DefaultCategory) ----

    public static void Trace(string message) => Log(LogLevel.Trace, message);
    public static void Debug(string message) => Log(LogLevel.Debug, message);
    public static void Info(string message) => Log(LogLevel.Information, message);
    public static void Warning(string message) => Log(LogLevel.Warning, message);
    public static void Error(string message, Exception? ex = null) => Log(LogLevel.Error, message, ex);
    public static void Error(Exception ex) => Log(LogLevel.Error, ex.Message, ex);
    public static void Critical(string message, Exception? ex = null) => Log(LogLevel.Critical, message, ex);

    // ---- Category support: use when routing/filtering per subsystem ----

    /// <summary>Gets an <see cref="ILogger"/> for a given category (shares the same providers and filtering as the convenience methods).</summary>
    public static ILogger GetLogger(string category)
    {
        EnsureInitialized();
        return _factory!.CreateLogger(category);
    }

    /// <summary>Gets an <see cref="ILogger"/> using the type's full name as the category.</summary>
    public static ILogger GetLogger<T>() => GetLogger(typeof(T).FullName ?? typeof(T).Name);

    // ---- Internal implementation ----

    // Entry point: pass the master switch (drop below MinimumLevel) → ensure initialized → write to all
    // providers → raise OnLog.
    private static void Log(LogLevel level, string message, Exception? exception = null)
    {
        if (!IsEnabled(level)) return;
        EnsureInitialized();
        _logger.Log(level, 0, message, exception, static (m, _) => m);
        RaiseEvent(level, message, exception);
    }

    // Auto-initialization: on the first log/GetLogger, build the default (provider-less) factory for the
    // quick path when not explicitly configured.
    private static void EnsureInitialized()
    {
        if (_factory != null) return; // fast path (volatile read)
        lock (SyncRoot)
        {
            if (_factory != null) return; // double-checked lock
            _factory = BuildFactory(null);
            _logger = _factory.CreateLogger(DefaultCategory);
        }
    }

    // Build the factory: relax the provider-wide minimum to Trace (so M.E.L's default Information filter
    // does not swallow Debug), leaving the real filtering to the MinimumLevel master switch. With no
    // configure, no providers are attached (only OnLog is available).
    private static ILoggerFactory BuildFactory(Action<ILoggingBuilder>? configure)
    {
        return LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            configure?.Invoke(builder);
        });
    }

    // Raise the event: snapshot subscribers first (to survive concurrent add/remove), then invoke
    // synchronously or post to UiContext; exceptions never leak.
    private static void RaiseEvent(LogLevel level, string message, Exception? exception)
    {
        var handler = OnLog;
        if (handler == null) return;

        try
        {
            if (UiContext is { } ui)
                ui.Post(_ => InvokeHandlerSafely(handler, level, message, exception), null);
            else
                InvokeHandlerSafely(handler, level, message, exception);
        }
        catch (Exception ex)
        {
            ReportSubscriberFailure(ex);
        }
    }

    private static void InvokeHandlerSafely(Action<LogLevel, string, Exception?> handler,
        LogLevel level, string message, Exception? exception)
    {
        try
        {
            handler(level, message, exception);
        }
        catch (Exception ex)
        {
            ReportSubscriberFailure(ex); // A subscriber exception must never break through to the caller.
        }
    }

    // A subscriber failure is written directly to the provider (not raising OnLog, to avoid recursion).
    private static void ReportSubscriberFailure(Exception exception)
    {
        var logger = _logger;
        if (ReferenceEquals(logger, NullLogger.Instance)) return;
        try
        {
            logger.Log(LogLevel.Error, 0, SubscriberFailureMessage, exception, static (m, _) => m);
        }
        catch
        {
            // A provider exception must also not be rethrown.
        }
    }
}
