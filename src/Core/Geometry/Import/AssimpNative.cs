using System;
using System.IO;
using RobotSimulation.Core.Utils;

namespace RobotSimulation.Core.Geometry.Import;

/// <summary>
/// Assimp native runtime preparation. AssimpNet 4.1 needs a loadable libdl.so on Linux, but some
/// distributions only ship libdl.so.2 — this adds a soft link in the output directory. Any entry
/// point that uses <see cref="AssimpModelLoader"/> to import a mesh should call
/// <see cref="EnsureRuntime"/> once first (a no-op on Windows/macOS). This class is not run
/// automatically by the library; the host/tool composition root triggers it explicitly, keeping the
/// Core library free of environment-bootstrap side effects.
/// </summary>
public static class AssimpNative
{
    /// <summary>
    /// Ensures the Assimp native library can load. On Linux this adds a compatibility link for libdl;
    /// on other platforms it always returns true.
    /// </summary>
    /// <returns>true = ready (no action needed / succeeded); false = system libdl not found, import may fail.</returns>
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
            Logger.Warning("System libdl.so.2 not found; Assimp model import may be unavailable.");
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
                Logger.Info($"Created compatibility link for Assimp: {target}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to create symbolic link {target}: {ex.Message}");
            }
        }

        return true;
    }
}
