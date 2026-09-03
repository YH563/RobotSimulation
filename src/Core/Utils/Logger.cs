using System;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;

namespace RobotSimulation.Core.Utils;

/// <summary>
/// 默认静态全局日志器
/// </summary>
public static class Logger
{
    private static ILogger _logger = NullLogger.Instance;
    private static ILoggerFactory? _factory;
    private static bool _initialized;
    
    public static event Action<LogLevel, string, Exception?>? OnLog;

    /// <summary>
    /// 初始化
    /// </summary>
    /// <param name="configure"></param>
    public static void Initialize(Action<ILoggingBuilder>? configure = null)
    {
        if (_initialized) return;

        var builder = LoggerFactory.Create(b =>
        {
            // 默认添加彩色控制台输出
            b.AddConsole(options => { options.FormatterName = ConsoleFormatterNames.Simple; });

            // 允许调用方添加更多提供者（如文件、EventLog 等）
            configure?.Invoke(b);
        });

        _factory = builder;
        _logger = builder.CreateLogger("Global");
        _initialized = true;
    }

}