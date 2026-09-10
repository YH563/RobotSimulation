using System;
using System.IO;
using System.Linq;

namespace RobotSimulation.Robot.Urdf;

/// <summary>
/// Locates URDF files: finds the .urdf to load from a CLI argument or the default asset directory.
/// This is distinct from <see cref="IAssetResolver"/> (which resolves mesh/texture references drawn
/// from inside a URDF) — here it is about "where the whole URDF file is", there it is "where the
/// resources referenced inside the URDF are". Called by the host composition root to decide whether to
/// load a robot.
/// </summary>
public static class UrdfLocator
{
    /// <summary>
    /// Model directory conventions searched under each root, in order: a host-local <c>Assets/Models</c>
    /// folder. Every host test keeps its own <c>Assets</c> tree next to its executable, so this is the
    /// only convention there is to guess.
    /// </summary>
    private static readonly string[] ModelDirectoryConventions =
    {
        Path.Combine("Assets", "Models"),
    };

    /// <summary>
    /// Finds the URDF file path to load.
    /// </summary>
    /// <param name="argument">
    /// A path explicitly given on the CLI; if it exists it is returned (after absolutizing). May be null.
    /// </param>
    /// <param name="extraSearchRoots">
    /// Extra search root directories (in addition to the default roots
    /// <see cref="AppContext.BaseDirectory"/> and the current directory); each root is searched
    /// recursively under the directory conventions in <see cref="ModelDirectoryConventions"/>. May be null.
    /// </param>
    /// <returns>The absolute path of the found .urdf, or null if none is found.</returns>
    public static string? Find(string? argument, params string?[]? extraSearchRoots)
    {
        if (!string.IsNullOrWhiteSpace(argument) && File.Exists(argument))
            return Path.GetFullPath(argument);

        string[] roots = new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() };
        if (extraSearchRoots is { Length: > 0 })
            roots = roots.Concat(extraSearchRoots.Where(r => !string.IsNullOrWhiteSpace(r)).Select(r => r!)).ToArray();

        foreach (string root in roots)
        {
            foreach (string convention in ModelDirectoryConventions)
            {
                string modelRoot = Path.Combine(root, convention);
                if (!Directory.Exists(modelRoot))
                    continue;

                foreach (string urdfPath in Directory.EnumerateFiles(modelRoot, "*.urdf", SearchOption.AllDirectories))
                    return urdfPath;
            }
        }

        return null;
    }
}
