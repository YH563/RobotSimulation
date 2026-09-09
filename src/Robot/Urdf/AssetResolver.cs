using System;
using System.IO;
using RobotSimulation.Core.Utils;

namespace RobotSimulation.Robot.Urdf;

/// <summary>
/// Asset-location extension point: resolves URDF mesh/texture reference strings into accessible absolute
/// paths. The description layer does not consume it — <see cref="RobotModel"/> calls it when building the
/// robot tree (material/mesh loading). The default implementation is <see cref="FileSystemAssetResolver"/>.
/// </summary>
public interface IAssetResolver
{
    /// <summary>
    /// Resolves an asset reference.
    /// </summary>
    /// <param name="uri">The raw reference string from the URDF (absolute/relative path or package:// form).</param>
    /// <param name="baseDirectory">The directory of the URDF file (may be null, in which case package:// cannot resolve relative to it).</param>
    /// <returns>An accessible absolute path.</returns>
    /// <exception cref="IOException">The default implementation throws when the resource is missing (file not found), leaving URDF path issues to the caller/user.</exception>
    string? Resolve(string uri, string? baseDirectory);
}

/// <summary>
/// Default filesystem implementation (simple rules, direct errors to help the user check their URDF):
/// <list type="bullet">
///   <item>Absolute path: located as an absolute path.</item>
///   <item>Relative path: combined relative to the URDF file's directory (baseDirectory).</item>
///   <item><c>package://package-name/relative-path</c>: strips the package prefix and looks for the
///   remaining relative path next to the URDF file (treating "next to the URDF" as the package root).</item>
/// </list>
/// After resolving, it still checks existence: if the file is missing it throws
/// <see cref="FileNotFoundException"/> directly so the user fixes the URDF filename or asset layout — no
/// silent skipping.
/// </summary>
public sealed class FileSystemAssetResolver : IAssetResolver
{
    public string? Resolve(string uri, string? baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(uri))
            throw new ArgumentException("Asset reference string cannot be empty.", nameof(uri));

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
            ? $" (expected directory: {dir})"
            : string.Empty;
        Logger.Error(
            $"URDF asset not found: {candidate} (original reference: {uri}){hint}. " +
            "Check the mesh/texture filename in the URDF, or move the asset to the corresponding directory and retry.");
        throw new FileNotFoundException(
            $"URDF asset not found: {candidate} (original reference: {uri}). " +
            "Check the mesh/texture filename in the URDF or the asset layout.", candidate);
    }

    /// <summary>package://package-name/rest → rest next to the URDF (using "next to the URDF file" as the package root).</summary>
    private static string ResolvePackageUri(string uri, string? baseDirectory)
    {
        const string prefix = "package://";
        int restStart = uri.IndexOf('/', prefix.Length);
        if (restStart < 0)
            throw new FileNotFoundException(
                $"package:// reference lacks a path: {uri}. The URDF mesh filename should be of the form package://package-name/relative/path.", uri);

        string relative = uri[(restStart + 1)..];
        if (string.IsNullOrEmpty(baseDirectory))
            throw new FileNotFoundException(
                $"package:// reference ({uri}) cannot be resolved: the URDF file directory (baseDirectory) is missing.", uri);

        return Path.Combine(baseDirectory, relative.Replace('/', Path.DirectorySeparatorChar));
    }

    private static string CombineRelative(string? baseDirectory, string relative)
        => string.IsNullOrEmpty(baseDirectory)
            ? Path.GetFullPath(relative)
            : Path.GetFullPath(Path.Combine(baseDirectory, relative));
}
