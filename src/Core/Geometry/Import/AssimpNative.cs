using System;
using System.IO;
using RobotSimulation.Core.Utils;

namespace RobotSimulation.Core.Geometry.Import;

/// <summary>
/// Assimp 原生运行环境准备。AssimpNet 4.1 在 Linux 下需要可加载的 libdl.so，
/// 而某些发行版只提供 libdl.so.2 —— 这里在输出目录补一个软链接。
/// 任何使用 <see cref="AssimpModelLoader"/> 导入 mesh 之前，程序入口应调用一次
/// <see cref="EnsureRuntime"/>（Windows/macOS 为空操作）。本类不随库自动执行，
/// 由宿主/工具的组装根显式触发，保持 Core 库本身无环境引导副作用。
/// </summary>
public static class AssimpNative
{
    /// <summary>
    /// 确保 Assimp 原生库可加载。Linux 上为 libdl 补兼容链接；其它平台恒返回 true。
    /// </summary>
    /// <returns>true = 已就绪（无需/成功）；false = 找不到系统 libdl，导入可能失败。</returns>
    public static bool EnsureRuntime()
    {
        if (!OperatingSystem.IsLinux())
            return true;

        string? real = new[]
        {
            "/usr/lib/x86_64-linux-gnu/libdl.so.2",
            "/lib/x86_64-linux-gnu/libdl.so.2",
            "/usr/lib64/libdl.so.2",
            "/lib64/libdl.so.2",
        }.FirstOrDefault(File.Exists);

        if (real is null)
        {
            Logger.Warning("找不到系统 libdl.so.2，Assimp 模型导入可能不可用。");
            return false;
        }

        foreach (string target in new[]
                 {
                     Path.Combine(AppContext.BaseDirectory, "libdl.so"),
                     Path.Combine(AppContext.BaseDirectory, "runtimes", "linux-x64", "native", "libdl.so"),
                 })
        {
            if (File.Exists(target))
                continue;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.CreateSymbolicLink(target, real);
                Logger.Info($"为 Assimp 创建兼容链接：{target}");
            }
            catch (Exception ex)
            {
                Logger.Error($"无法创建符号链接 {target}：{ex.Message}");
            }
        }

        return true;
    }
}