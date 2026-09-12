using System;
using System.IO;

namespace RobotSimulation.Tests;

/// <summary>
/// Locates the two asset trees the checks read. Nothing is copied to the output directory: the tests only
/// ever run from a repo checkout (<c>dotnet test</c> / IDE), so they walk up from the test assembly to the
/// repository root and read the trees in place. That keeps the model samples out of a third copy and means
/// an edited fixture is picked up without a rebuild.
/// </summary>
internal static class TestAssets
{
    /// <summary>Repository root — the directory containing <c>RobotSimulation.sln</c>.</summary>
    internal static string RepoRoot { get; } = FindRepoRoot();

    /// <summary>
    /// The hosts' shared model tree (<c>src/BareWindowTest/Assets</c>): the repo's canonical test data, and
    /// the exact files both host tests load. The Avalonia host carries a byte-identical copy, so exercising
    /// this tree is exercising what a host run sees.
    /// </summary>
    internal static string HostAssets { get; } = Path.Combine(RepoRoot, "src", "BareWindowTest", "Assets");

    /// <summary>
    /// This project's own fixture: a standard ROS workspace — <c>my_pkg/{package.xml, urdf/, meshes/}</c> —
    /// the one layout the hosts do not carry. It is the case a name-based <c>package://</c> lookup cannot
    /// load, and the reason <c>assetDirectory</c> exists.
    /// </summary>
    internal static string RosPackage { get; } =
        Path.Combine(RepoRoot, "src", "RobotSimulation.Tests", "Assets", "RosWorkspace", "my_pkg");

    /// <summary>A <c>Models/…</c> path under <see cref="HostAssets"/>.</summary>
    internal static string Model(string relativePath) => Path.Combine(HostAssets, "Models", relativePath);

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "RobotSimulation.sln")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Repository root not found: no RobotSimulation.sln is above {AppContext.BaseDirectory}. " +
            "These checks read the shared asset trees from the repo checkout, so they only run from one.");
    }
}
