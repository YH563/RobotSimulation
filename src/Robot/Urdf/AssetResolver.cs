using System;
using System.IO;

namespace RobotSimulation.Robot.Urdf;

/// <summary>
/// 资源定位扩展点：把 URDF 中 mesh/texture 等引用串解析为可访问的绝对路径。
/// 描述层不消费它——由渲染/导入层（RobotModel.CreateGameObject）在需要读文件时调用。
/// </summary>
public interface IAssetResolver
{
    /// <summary>
    /// 解析资源引用。
    /// </summary>
    /// <param name="uri">URDF 中的原始引用串（绝对/相对路径或 package:// 形式）。</param>
    /// <param name="baseDirectory">URDF 文件所在目录（可为 null）。</param>
    /// <returns>可访问的绝对路径；无法解析时返回 null（调用方决定告警或跳过）。</returns>
    string? Resolve(string uri, string? baseDirectory);
}

/// <summary>
/// 默认文件系统实现：绝对路径原样返回；相对路径以 <paramref name="baseDirectory"/> 为基准合并；
/// <c>package://</c> 形式暂不支持，返回 null。
/// </summary>
public sealed class FileSystemAssetResolver : IAssetResolver
{
    public string? Resolve(string uri, string? baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(uri))
            return null;

        if (uri.StartsWith("package://", StringComparison.OrdinalIgnoreCase))
            return null;

        if (Path.IsPathRooted(uri))
            return Path.GetFullPath(uri);

        string combined = string.IsNullOrEmpty(baseDirectory)
            ? uri
            : Path.Combine(baseDirectory, uri);
        return Path.GetFullPath(combined);
    }
}
