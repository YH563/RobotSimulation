using System.Numerics;
using RobotSimulation.Core.Rendering;
using RobotSimulation.Core.Scene;
using RobotSimulation.OpenGL.Resources;

namespace RobotSimulation.Tests;

/// <summary>
/// Line width is scene-side data (a node property) plus one embedded shader stage; what is pinned here is the
/// default a host inherits and that the Line pass actually carries the geometry shader that turns the width into
/// screen-space quads. The drawing itself needs a GL context and is covered by the host smoke runs.
/// </summary>
public class LineWidthTests
{
    private static readonly Vector3[] Segment = { Vector3.Zero, Vector3.UnitX };

    [Fact]
    public void ACurveDefaultsToOnePixel()
    {
        var curve = new Curve(Segment);

        Assert.Equal(1f, curve.LineWidth);
    }

    [Fact]
    public void ACurveTakesItsLineWidthFromTheConstructor()
    {
        var curve = new Curve(Segment, lineWidth: 4f);

        Assert.Equal(4f, curve.LineWidth);
    }

    [Fact]
    public void LineWidthCanBeChangedAfterConstruction()
    {
        var curve = new Curve(Segment);

        curve.LineWidth = 2.5f;

        Assert.Equal(2.5f, curve.LineWidth);
    }

    [Fact]
    public void TheLinePassShipsTheGeometryShaderThatExpandsSegments()
    {
        (string vertex, string? geometry, string fragment) = EmbeddedShaders.Get(RenderPassKind.Line);

        Assert.False(string.IsNullOrWhiteSpace(vertex));
        Assert.False(string.IsNullOrWhiteSpace(fragment));
        Assert.NotNull(geometry);
        Assert.Contains("layout (lines) in", geometry);
    }
}
