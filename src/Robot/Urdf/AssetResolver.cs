using System;
using System.Collections.Generic;
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
/// Default filesystem implementation: turns a URDF reference into an existing absolute path by trying a
/// short, ordered list of roots and returning the first hit — never guessing a file, never skipping one.
/// <list type="bullet">
///   <item>Absolute path: used as-is.</item>
///   <item>Relative path: searched under <see cref="AssetDirectory"/> first (when one is set), then under
///   <c>baseDirectory</c> — the URDF file's own directory. This is MuJoCo's <c>meshdir</c> idea: an
///   explicitly configured root wins, the URDF's own folder stays the default.</item>
///   <item><c>package://package-name/rest</c>: the package name is dropped and <c>rest</c> is then searched
///   like a relative path. The name is deliberately never matched against a directory name — it is a ROS
///   package id, and URDFs routinely reference <c>package://rus_sim_driver/…</c> from a folder called
///   <c>fairino3_v6</c>, so it carries no usable information on disk.</item>
/// </list>
/// A miss on every root is a hard error (<see cref="FileNotFoundException"/>) that lists every path tried,
/// so a broken reference is fixed in the URDF or by pointing <see cref="AssetDirectory"/> at the right
/// folder — it is never silently skipped.
/// </summary>
public sealed class FileSystemAssetResolver : IAssetResolver
{
    private const string PackagePrefix = "package://";

    /// <summary>
    /// Extra root searched before the URDF file's own directory (the equivalent of MuJoCo's <c>meshdir</c>),
    /// or null when no extra root is configured. Always absolute.
    /// </summary>
    public string? AssetDirectory { get; }

    /// <summary>
    /// Creates the resolver.
    /// </summary>
    /// <param name="assetDirectory">
    /// Extra directory to look for mesh/texture files in, tried before the URDF file's directory. A relative
    /// value is absolutized here against the current directory, so the search does not move if the process
    /// later changes its working directory. Null/blank = no extra root (the URDF directory is used alone).
    /// </param>
    public FileSystemAssetResolver(string? assetDirectory = null)
    {
        AssetDirectory = string.IsNullOrWhiteSpace(assetDirectory)
            ? null
            : Path.GetFullPath(assetDirectory);
    }

    /// <summary>
    /// Resolves an asset reference to an absolute path on disk, or null when nothing matches.
    /// A <c>package://</c> prefix is dropped (the package name is never matched against a directory);
    /// an absolute path is used as-is, and a relative one is tried against the fallback chain described
    /// on this class. Every path that was tried is listed in the exception when nothing matches.
    /// </summary>
    /// <param name="uri">The reference as written in the URDF (e.g. <c>package://pkg/meshes/a.stl</c>).</param>
    /// <param name="baseDirectory">Directory of the referencing file (the URDF), used as the first fallback root.</param>
    /// <exception cref="ArgumentException"><paramref name="uri"/> is null or blank.</exception>
    /// <exception cref="FileNotFoundException">No candidate path exists.</exception>
    public string? Resolve(string uri, string? baseDirectory)
    {
        if (string.IsNullOrWhiteSpace(uri))
            throw new ArgumentException("Asset reference string cannot be empty.", nameof(uri));

        string reference = uri.StartsWith(PackagePrefix, StringComparison.OrdinalIgnoreCase)
            ? StripPackagePrefix(uri)
            : uri;

        IReadOnlyList<string> candidates = Path.IsPathRooted(reference)
            ? new[] { Path.GetFullPath(reference) }
            : BuildCandidates(reference, baseDirectory);

        foreach (string candidate in candidates)
        {
            if (!File.Exists(candidate))
                continue;

            Logger.Debug($"[AssetResolver] '{uri}' → {candidate}");
            return candidate;
        }

        string tried = string.Join(" | ", candidates);
        string message =
            $"URDF asset not found: '{uri}'. Tried: {tried}. " +
            "Check the mesh/texture filename in the URDF, or point AssetDirectory at the folder that contains it and retry.";
        Logger.Error(message);
        throw new FileNotFoundException(message, candidates[0]);
    }

    /// <summary><c>package://package-name/rest</c> → <c>rest</c>; the package name is deliberately discarded.</summary>
    private static string StripPackagePrefix(string uri)
    {
        int restStart = uri.IndexOf('/', PackagePrefix.Length);
        if (restStart < 0 || restStart == uri.Length - 1)
            throw new FileNotFoundException(
                $"package:// reference has no usable path: {uri}. " +
                "The URDF mesh filename should be of the form package://package-name/relative/path.",
                uri);

        return uri[(restStart + 1)..];
    }

    /// <summary>
    /// Ordered, de-duplicated candidates for a relative reference: the configured extra root first, then the
    /// URDF file's directory. With no root at all (no base directory and no <see cref="AssetDirectory"/>) the
    /// reference falls back to the current directory, which is what a bare relative path always meant.
    /// </summary>
    private IReadOnlyList<string> BuildCandidates(string reference, string? baseDirectory)
    {
        var roots = new List<string>(2);
        AddRoot(roots, AssetDirectory);
        AddRoot(roots, baseDirectory);

        if (roots.Count == 0)
            return new[] { Path.GetFullPath(reference) };

        var candidates = new List<string>(roots.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (string root in roots)
        {
            string candidate = Path.GetFullPath(Path.Combine(root, reference));
            if (seen.Add(candidate))
                candidates.Add(candidate); // Identical roots (e.g. assetDirectory == baseDirectory) collapse to one candidate.
        }

        return candidates;
    }

    private static void AddRoot(List<string> roots, string? root)
    {
        if (string.IsNullOrWhiteSpace(root))
            return;

        string full = Path.GetFullPath(root);
        if (!roots.Contains(full, StringComparer.Ordinal))
            roots.Add(full);
    }
}
