using System;
using System.IO;
using System.Linq;

namespace RobotSimulation.Robot.Urdf;

/// <summary>
/// URDF 文件定位：从命令行参数或默认资产目录中找出要加载的 .urdf。
/// 与 <see cref="IAssetResolver"/>（mesh/texture 引用解析）分工不同——
/// 这里管的是"整个 URDF 文件在哪"，那里管的是"URDF 内部引用的资源在哪"。
/// 由宿主组装根调用，判断是否值得加载机器人。
/// </summary>
public static class UrdfLocator
{
    /// <summary>
    /// 定位要加载的 URDF 文件路径。
    /// </summary>
    /// <param name="argument">
    /// 命令行显式给出的路径；存在则直接使用（绝对化后返回）。可为 null。
    /// </param>
    /// <param name="extraSearchRoots">
    /// 额外的搜索根目录（在默认根 <see cref="AppContext.BaseDirectory"/> 与当前目录之外）；
    /// 每个根下会在其 <c>Assets/Models</c> 子目录中递归查找。可为 null。
    /// </param>
    /// <returns>找到的 .urdf 绝对路径；找不到返回 null。</returns>
    public static string? Find(string? argument, params string?[]? extraSearchRoots)
    {
        if (!string.IsNullOrWhiteSpace(argument) && File.Exists(argument))
            return Path.GetFullPath(argument);

        string[] roots = new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() };
        if (extraSearchRoots is { Length: > 0 })
            roots = roots.Concat(extraSearchRoots.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r!)).ToArray();

        foreach (string root in roots)
        {
            string modelRoot = Path.Combine(root, "Assets", "Models");
            if (!Directory.Exists(modelRoot))
                continue;

            foreach (string urdfPath in Directory.EnumerateFiles(modelRoot, "*.urdf", SearchOption.AllDirectories))
                return urdfPath;
        }

        return null;
    }
}