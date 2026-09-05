using System;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace RobotSimulation.Core.Utils;

/// <summary>
/// 进程级静态内部日志器：对 <see cref="Microsoft.Extensions.Logging"/> 的薄门面，各模块无需 DI 即可直接使用。
/// </summary>
/// <remarks>
/// 关键契约：
/// 1. 首次日志前若未显式 <see cref="Initialize"/>，会自动用「默认彩色控制台」初始化。
///    宿主自定义 Provider 必须在任何日志输出之前于组合根显式调用（初始化后幂等）。
/// 2. 显式传入 configure 时，Provider 配置完全由调用方接管（不再自动附加控制台）。
/// 3. 级别过滤统一由 <see cref="MinimumLevel"/> 总闸控制（默认 Debug，可运行期调整）。
/// 4. 便捷方法固定用分类 <see cref="DefaultCategory"/>；需要独立分类请用 <see cref="GetLogger{T}"/>。
/// 5. 进程退出前调用 <see cref="Shutdown"/> 释放 LoggerFactory。
/// </remarks>
public static class Logger
{
    /// <summary>便捷方法使用的默认日志分类。</summary>
    public const string DefaultCategory = "Global";

    // OnLog 订阅者异常时写给 Provider 的提示消息（直接走 Provider，避免递归触发事件）
    private const string SubscriberFailureMessage = "OnLog 订阅者抛出异常，已由日志器吞掉并继续（请订阅方自行排查）。";

    // ---- 初始化状态 ----
    // double-checked locking；_factory/_logger 置 volatile，保证多线程下的可见性
    private static readonly object SyncRoot = new();
    private static volatile ILoggerFactory? _factory;
    private static volatile ILogger _logger = NullLogger.Instance;

    // LogLevel 底层是 int，用 Volatile.Read/Write 做无锁读写，支持运行期随时调整总闸
    private static int _minimumLevel = (int)LogLevel.Debug;

    /// <summary>每一条通过总闸的日志都会触发。设置 <see cref="UiContext"/> 后回调会切到 UI 线程。
    /// 订阅者抛异常不会影响调用方（会被捕获并记一条日志）。</summary>
    public static event Action<LogLevel, string, Exception?>? OnLog;

    /// <summary>设置后，<see cref="OnLog"/> 回调会 Post 到该 SynchronizationContext（通常为 UI 线程）。</summary>
    public static SynchronizationContext? UiContext { get; set; }

    /// <summary>运行期日志级别总闸：低于该级别的不写 Provider、也不触发 <see cref="OnLog"/>。线程安全。</summary>
    public static LogLevel MinimumLevel
    {
        get => (LogLevel)Volatile.Read(ref _minimumLevel);
        set => Volatile.Write(ref _minimumLevel, (int)value);
    }

    /// <summary>查询该级别当前是否会被记录（热路径可用它避免提前拼字符串）。</summary>
    public static bool IsEnabled(LogLevel level) => level >= MinimumLevel;

    /// <summary>显式初始化（线程安全、幂等）。configure 为空用默认彩色控制台；否则 Provider 由调用方完全接管。</summary>
    public static void Initialize(Action<ILoggingBuilder>? configure = null)
    {
        lock (SyncRoot)
        {
            if (_factory != null) return;
            _factory = BuildFactory(configure);
            _logger = _factory.CreateLogger(DefaultCategory);
        }
    }

    /// <summary>释放内部 LoggerFactory（冲刷 Provider 写队列），之后允许再次 <see cref="Initialize"/>。Host 退出前调用。</summary>
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

    // ---- 便捷日志方法（分类固定为 DefaultCategory）----

    public static void Trace(string message) => Log(LogLevel.Trace, message);
    public static void Debug(string message) => Log(LogLevel.Debug, message);
    public static void Info(string message) => Log(LogLevel.Information, message);
    public static void Warning(string message) => Log(LogLevel.Warning, message);
    public static void Error(string message, Exception? ex = null) => Log(LogLevel.Error, message, ex);
    public static void Error(Exception ex) => Log(LogLevel.Error, ex.Message, ex);
    public static void Critical(string message, Exception? ex = null) => Log(LogLevel.Critical, message, ex);

    // ---- 分类（category）支持：需要按子系统路由/过滤时使用 ----

    /// <summary>以指定分类获取 <see cref="ILogger"/>（与便捷方法共享同一套 Provider 与过滤）。</summary>
    public static ILogger GetLogger(string category)
    {
        EnsureInitialized();
        return _factory!.CreateLogger(category);
    }

    /// <summary>以类型全名作为分类获取 <see cref="ILogger"/>。</summary>
    public static ILogger GetLogger<T>() => GetLogger(typeof(T).FullName ?? typeof(T).Name);

    // ---- 内部实现 ----

    // 总入口：过总闸（低于 MinimumLevel 直接丢弃）→ 确保已初始化 → 写全部 Provider → 触发 OnLog
    private static void Log(LogLevel level, string message, Exception? exception = null)
    {
        if (!IsEnabled(level)) return;
        EnsureInitialized();
        _logger.Log(level, 0, message, exception, static (m, _) => m);
        RaiseEvent(level, message, exception);
    }

    // 自动初始化：首个日志/GetLogger 触发时按默认（仅控制台）补建，供未显式配置的快速路径使用
    private static void EnsureInitialized()
    {
        if (_factory != null) return; // fast path（volatile 读）
        lock (SyncRoot)
        {
            if (_factory != null) return; // 双检锁
            _factory = BuildFactory(null);
            _logger = _factory.CreateLogger(DefaultCategory);
        }
    }

    // 建工厂：Provider 统一放宽到 Trace（避免 M.E.L 默认 Information 过滤把 Debug 吞掉），
    // 级别过滤交给 MinimumLevel 总闸；无 configure 时附加默认彩色控制台
    private static ILoggerFactory BuildFactory(Action<ILoggingBuilder>? configure)
    {
        return LoggerFactory.Create(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Trace);
            if (configure == null)
                builder.AddConsole(options => options.FormatterName = ConsoleFormatterNames.Simple);
            else
                configure(builder);
        });
    }

    // 触发事件：先快照订阅者（防并发期间事件被增删），再同步调用或 Post 到 UiContext；异常全程不外泄
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
            ReportSubscriberFailure(ex); // 订阅者异常绝不能击穿日志调用方
        }
    }

    // 订阅者异常直接写 Provider（不触发 OnLog，避免递归）
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
            // Provider 自身异常也不能再抛出
        }
    }
}