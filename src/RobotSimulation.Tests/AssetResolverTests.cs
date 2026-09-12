using System;
using System.IO;
using RobotSimulation.Robot.Urdf;
using Xunit;

namespace RobotSimulation.Tests;

/// <summary>
/// <see cref="FileSystemAssetResolver"/> path resolution: the ordered root chain (<c>AssetDirectory</c>
/// first, then the URDF file's own directory), <c>package://</c> handling, and the hard-failure contract
/// on a miss.
/// </summary>
public sealed class AssetResolverTests
{
    /// <summary>The sample whose URDF says <c>package://rus_sim_driver/…</c> but lives in <c>fairino3_v6/</c>.</summary>
    private static string FairinoDirectory => TestAssets.Model("fairino3_v6");

    /// <summary>The sample whose <c>package://</c> name does happen to match its folder name.</summary>
    private static string TutorialDirectory => TestAssets.Model("urdf_tutorial");

    [Fact]
    public void PackageReference_IgnoresThePackageName_AndResolvesUnderTheUrdfDirectory()
    {
        // A package id is a ROS identity, not a folder name: fairino3_v6.urdf references
        // package://rus_sim_driver/… while the files sit in fairino3_v6/meshes/. Matching the name would
        // fail here and can never add information on disk, so the prefix is dropped and the rest is a
        // plain relative path.
        string? resolved = new FileSystemAssetResolver()
            .Resolve("package://rus_sim_driver/meshes/base_link.STL", FairinoDirectory);

        Assert.Equal(Path.Combine(FairinoDirectory, "meshes", "base_link.STL"), resolved);
    }

    [Fact]
    public void PackageReference_ResolvesWhenThePackageNameMatchesTheFolder()
    {
        string? resolved = new FileSystemAssetResolver()
            .Resolve("package://urdf_tutorial/meshes/l_finger.dae", TutorialDirectory);

        Assert.Equal(Path.Combine(TutorialDirectory, "meshes", "l_finger.dae"), resolved);
    }

    [Fact]
    public void AbsoluteReference_IsUsedAsIs()
    {
        string absolute = Path.Combine(TutorialDirectory, "meshes", "l_finger.dae");

        Assert.Equal(absolute, new FileSystemAssetResolver().Resolve(absolute, null));
    }

    [Fact]
    public void AssetDirectory_IsTriedBeforeTheUrdfDirectory()
    {
        // The stated purpose of AssetDirectory (MuJoCo's meshdir): an explicitly configured root wins, so a
        // standard ROS layout (pkg/{urdf,meshes}) loads even though no meshes sit next to the URDF.
        string? resolved = new FileSystemAssetResolver(TestAssets.RosPackage)
            .Resolve("package://my_pkg/meshes/cube.stl", "/does/not/exist");

        Assert.Equal(Path.Combine(TestAssets.RosPackage, "meshes", "cube.stl"), resolved);
    }

    [Fact]
    public void AssetDirectoryMiss_FallsBackToTheUrdfDirectory()
    {
        string? resolved = new FileSystemAssetResolver(TestAssets.RosPackage)
            .Resolve("package://rus_sim_driver/meshes/base_link.STL", FairinoDirectory);

        Assert.Equal(Path.Combine(FairinoDirectory, "meshes", "base_link.STL"), resolved);
    }

    [Fact]
    public void IdenticalRoots_CollapseToASingleCandidate()
    {
        // assetDirectory == the URDF's directory must not produce the same path twice.
        string? resolved = new FileSystemAssetResolver(TutorialDirectory)
            .Resolve("package://p/meshes/l_finger.dae", TutorialDirectory);

        Assert.Equal(Path.Combine(TutorialDirectory, "meshes", "l_finger.dae"), resolved);
    }

    [Fact]
    public void MissingAsset_ThrowsListingEveryTriedPath()
    {
        string assetRoot = Path.Combine(Path.GetTempPath(), "RobotSimulation.Tests", "missing-asset-root");
        string urdfRoot = Path.Combine(Path.GetTempPath(), "RobotSimulation.Tests", "missing-urdf-root");

        // A miss is a hard error: it must name every root that was tried (so a broken reference is fixed in
        // the URDF rather than silently rendered as nothing) and point at the knob that fixes it.
        FileNotFoundException ex = Assert.Throws<FileNotFoundException>(
            () => new FileSystemAssetResolver(assetRoot).Resolve("meshes/missing.stl", urdfRoot));

        Assert.Contains(assetRoot, ex.Message);
        Assert.Contains(urdfRoot, ex.Message);
        Assert.Contains("AssetDirectory", ex.Message);
    }
}
