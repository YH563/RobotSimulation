using System;
using System.IO;
using RobotSimulation.Core.Utils;

namespace RobotSimulation.Robot.Urdf;

/// <summary>
/// 资源定位扩展点：把 URDF 中 mesh/texture 等引用串解析为可访问的绝对路径。
/// 描述层不消费它——由 <see cref="RobotModel"/> 在构建机器人树（材质/mesh 载入）时调用。
/// 默认实现见 <see cref="FileSystemAssetResolver"/>。
/// </summary>
public interface IAssetResolver
{
    /// <summary>
    /// 解析资源引用。
    /// </summary>
    /// <param name="uri">URDF 中的原始引用串（绝对/相对路径或 package:// 形式）。</param>
    /// <param name="baseDirectory">URDF 文件所在目录（可为 null，此时 package:// 无法按同级解析）。</param>
    /// <returns>可访问的绝对路径。</returns>
    /// <exception cref="IOException">默认实现找不到资源时抛出（文件不存在），交由调用方/用户处理 URDF 路径问题。</exception>
    string? Resolve(string uri, string? baseDirectory);
}

/// <summary>
/// 默认文件系统实现（规则简单、报错直接，便于用户自查 URDF）：
/// <list type="bullet">
///   <item>绝对路径：按绝对路径定位。</item>
///   <item>相对路径：以 URDF 文件所在目录（baseDirectory）为基准合并。</item>
///   <item><c>package://包名/相对路径</c>：剥掉包名前缀，把剩余相对路径在 URDF 同级目录下查找
///   （即以"URDF 文件旁是否存在该相对路径"为 package 定位规则）。</item>
/// </list>
/// 定位到目标后仍做存在性校验：文件不存在直接 <see cref="FileNotFoundException"/> 报错，
/// 让用户自行修正 URDF 里的 filename 或资源布局——不做静默跳过。
/// </summary>
public sealed class FileSystemAssetResolver : IAssetResolver
{
    public string? Resolve(string uri, string? baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(uri))
            throw new ArgumentException("资源引用串不能为空。", nameof(uri));

        string candidate;
        if (uri.StartsWith("package://", StringComparison.OrdinalIgnoreCase))
        {
            candidate = ResolvePackageUri(uri, baseDirectory);
        }
        else if (Path.IsPathRooted(uri))
        {
            candidate = Path.GetFullPath(uri);
        }
        else
        {
            candidate = CombineRelative(baseDirectory, uri);
        }

        if (File.Exists(candidate))
        {
            Logger.Debug($"[AssetResolver] '{uri}' → {candidate}");
            return candidate;
        }

        string hint = Path.GetDirectoryName(candidate) is { } dir && !string.IsNullOrEmpty(dir)
            ? $"（期望目录：{dir}）"
            : string.Empty;
        Logger.Error(
            $"URDF 资源不存在：{candidate}（原始引用：{uri}）{hint}。" +
            "请检查 URDF 的 mesh/texture filename，或将资源放到对应目录后重试。");
        throw new FileNotFoundException(
            $"URDF 资源不存在：{candidate}（原始引用：{uri}）。" +
            "请检查 URDF 的 mesh/texture filename 或资源布局。", candidate);
    }

    /// <summary>package://包名/rest → URDF 同级目录下的 rest（以 URDF 文件旁为 package 根）。</summary>
    private static string ResolvePackageUri(string uri, string? baseDirectory)
    {
        const string prefix = "package://";
        int restStart = uri.IndexOf('/', prefix.Length);
        if (restStart < 0)
            throw new FileNotFoundException(
                $"package:// 引用缺少路径：{uri}。URDF 中 mesh filename 应为 package://包名/相对路径 形式。", uri);

        string relative = uri[(restStart + 1)..];
        if (string.IsNullOrEmpty(baseDirectory))
            throw new FileNotFoundException(
                $"package:// 引用（{uri}）无法解析：缺少 URDF 文件所在目录（baseDirectory）。", uri);

        return Path.Combine(baseDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string CombineRelative(string? baseDirectory, string relative)
        => string.IsNullOrEmpty(baseDirectory)
            ? Path.GetFullPath(relative)
            : Path.GetFullPath(Path.Combine(baseDirectory, relative));
}

