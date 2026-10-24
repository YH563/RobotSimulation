using System.Numerics;
using RobotSimulation.Core.Geometry;
using RobotSimulation.Core.Geometry.Collision;
using RobotSimulation.Core.Rendering;
using RobotSimulation.Core.Scene;
using RobotSimulation.OpenGL.Resources;

namespace RobotSimulation.Tests;

/// <summary>
/// Collision debug data without a GL context: the fill nodes use the translucent Debug pass and the
/// contact marker uses the line pass. Actual blended drawing is covered by the host smoke runs.
/// </summary>
public class CollisionDebugTests
{
    [Fact]
    public void FillNodeUsesTheDebugPassAndIsNotPickable()
    {
        var instance = new CollisionShapeInstance(new BoxShape(Vector3.One), Matrix4x4.Identity);
        GameObject node = CollisionDebug.CreateFill(instance, Matrix4x4.Identity);

        Assert.NotNull(node.MeshData);
        Assert.True(node.MeshData!.TriangleCount > 0);
        Assert.Equal(RenderPassKind.Debug, node.MaterialData!.PassKind);
        Assert.True(node.MaterialData.BaseColor.W < 1f);
        Assert.False(node.Pickable);

        GameObject outline = Assert.Single(node.Transform.Children).Owner;
        Assert.NotNull(outline.LineData);
        Assert.Equal(RenderPassKind.Debug, outline.MaterialData!.PassKind);
        Assert.True(outline.LineWidth > 1f);
        Assert.False(outline.Pickable);
    }

    [Fact]
    public void LocalFillIncludesTheShapeLocalPose()
    {
        var instance = new CollisionShapeInstance(new SphereShape(0.5f),
            Matrix4x4.CreateTranslation(1f, 2f, 3f));
        GameObject node = CollisionDebug.CreateLocalFill(instance);

        Assert.Equal(new Vector3(1f, 2f, 3f), node.Transform.Position);
    }

    [Fact]
    public void MeshShapeFillUsesTheActualMesh()
    {
        var mesh = new MeshData();
        uint a = mesh.AddVertex(Vector3.Zero, Vector3.UnitZ, Vector2.Zero, Vector3.UnitX);
        uint b = mesh.AddVertex(Vector3.UnitX, Vector3.UnitZ, Vector2.Zero, Vector3.UnitX);
        uint c = mesh.AddVertex(Vector3.UnitY, Vector3.UnitZ, Vector2.Zero, Vector3.UnitX);
        mesh.AddTriangle(a, b, c);

        var instance = new CollisionShapeInstance(new MeshShape(mesh), Matrix4x4.Identity);
        GameObject node = CollisionDebug.CreateFill(instance, Matrix4x4.Identity);

        Assert.NotNull(node.MeshData);
        Assert.True(node.MeshData!.TriangleCount > 0);
    }

    [Fact]
    public void ContactMarkerUsesTheLinePass()
    {
        var contact = new CollisionContact(new GameObject(name: "a"), new GameObject(name: "b"),
            Vector3.Zero, Vector3.UnitZ, 0.1f);
        GameObject marker = CollisionDebug.CreateContactMarker(contact);

        Assert.Equal(RenderPassKind.Line, marker.MaterialData!.PassKind);
        Assert.False(marker.Pickable);
    }

    [Fact]
    public void TheDebugPassShipsAShader()
    {
        (string vertex, string? geometry, string fragment) = EmbeddedShaders.Get(RenderPassKind.Debug);

        Assert.False(string.IsNullOrWhiteSpace(vertex));
        Assert.False(string.IsNullOrWhiteSpace(fragment));
        Assert.Null(geometry);
        Assert.Contains("uColor", fragment);
    }
}
