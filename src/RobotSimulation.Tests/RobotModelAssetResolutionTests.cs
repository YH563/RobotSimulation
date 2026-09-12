using System;
using System.IO;
using RobotSimulation.Core.Scene;
using RobotSimulation.Robot;
using RobotSimulation.Robot.Urdf;
using Xunit;

namespace RobotSimulation.Tests;

/// <summary>
/// End-to-end URDF loading through <see cref="RobotModel"/>: the resolver chain as the robot layer
/// actually uses it, and the <c>resolver</c> / <c>assetDirectory</c> argument contract.
/// </summary>
public sealed class RobotModelAssetResolutionTests
{
    [Fact]
    public void ParseFile_DefaultLayout_ImportsEveryMeshReferencedThroughAMismatchedPackageName()
    {
        // fairino3_v6 references package://rus_sim_driver/… for all 7 meshes from a folder of a different
        // name, so the default chain (the URDF's own directory, nothing else) must resolve every one.
        RobotModel model = RobotModel.ParseFile(TestAssets.Model("fairino3_v6/fairino3_v6.urdf"));

        Assert.Equal("fairino3_v6_robot", model.RobotName);
        Assert.Equal(7, model.Description.Links.Count);
        Assert.Equal(6, model.DrivableJointCount);
        Assert.Equal(7, MeshNodeCount(model));
    }

    [Fact]
    public void ParseFile_FormatSample_ImportsEveryMeshFormatTheImporterClaims()
    {
        // The same cube exported as STL / OBJ+MTL / COLLADA / glTF / PLY: one parse proves which formats
        // the importer resolves for us (PLY included — it is a mesh here, not a point cloud).
        RobotModel model = RobotModel.ParseFile(TestAssets.Model("formats/formats.urdf"));

        Assert.Equal("mesh_formats_demo", model.RobotName);
        Assert.Equal(5, model.Description.Links.Count);
        Assert.Equal(5, MeshNodeCount(model));
    }

    [Fact]
    public void ParseFile_TutorialVisualSample_ImportsEveryVisual()
    {
        RobotModel model = RobotModel.ParseFile(TestAssets.Model("urdf_tutorial/05-visual.urdf"));

        Assert.Equal("visual", model.RobotName);
        Assert.Equal(16, model.Description.Links.Count);
        // 16 visuals: 4 COLLADA gripper meshes (left/right gripper + tip) plus 12 primitives.
        Assert.Equal(16, MeshNodeCount(model));
    }

    [Fact]
    public void ParseFile_RosLayout_WithoutAssetDirectory_Fails()
    {
        // A standard ROS layout keeps meshes/ as a sibling of urdf/, so nothing resolves relative to the
        // URDF's own directory. The failure has to be loud and name the knob that fixes it.
        string urdf = Path.Combine(TestAssets.RosPackage, "urdf", "robot.urdf");

        FileNotFoundException ex = Assert.Throws<FileNotFoundException>(() => RobotModel.ParseFile(urdf));

        Assert.Contains("AssetDirectory", ex.Message);
    }

    [Fact]
    public void ParseFile_RosLayout_WithAssetDirectoryAtThePackageRoot_Loads()
    {
        // The fix for the case above: point assetDirectory at the package root.
        RobotModel model = RobotModel.ParseFile(
            Path.Combine(TestAssets.RosPackage, "urdf", "robot.urdf"),
            assetDirectory: TestAssets.RosPackage);

        Assert.Equal("ros_layout_bot", model.RobotName);
        Assert.Single(model.Description.Links);
        Assert.Equal(1, MeshNodeCount(model));
    }

    [Fact]
    public void ParseFile_ExplicitResolver_StillWorks()
    {
        RobotModel model = RobotModel.ParseFile(
            TestAssets.Model("urdf_tutorial/02-multipleshapes.urdf"), new FileSystemAssetResolver());

        Assert.Equal("multipleshapes", model.RobotName);
    }

    [Fact]
    public void ParseFile_ResolverAndAssetDirectoryTogether_AreRejected()
    {
        // Two ways of saying "where the assets are" cannot both win; the error says which one to drop.
        ArgumentException ex = Assert.Throws<ArgumentException>(() => RobotModel.ParseFile(
            TestAssets.Model("primitives.urdf"), new FileSystemAssetResolver(), assetDirectory: "/tmp"));

        Assert.Contains("custom IAssetResolver", ex.Message);
    }

    /// <summary>Counts the nodes in the robot tree that carry imported CPU geometry.</summary>
    private static int MeshNodeCount(GameObject root)
    {
        int count = root.MeshData is not null ? 1 : 0;
        foreach (Transform child in root.Transform.Children)
            count += MeshNodeCount(child.Owner);

        return count;
    }
}
