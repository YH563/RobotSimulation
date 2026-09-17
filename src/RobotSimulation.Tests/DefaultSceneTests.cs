using System;
using System.Linq;
using System.Numerics;
using RobotSimulation.Core.Geometry;
using RobotSimulation.Core.Scene;
using Xunit;

namespace RobotSimulation.Tests;

/// <summary>
/// The default scene's assembly rules, checked headless: what a host gets from <c>new SceneGraph()</c> without
/// writing any setup code, how an axes set is sized once it is a scene object (a ruler that can be made to stand out
/// past the model) rather than a screen-space marker, and what selecting an object gives back — the highlight plus
/// that node's own axes.
/// </summary>
public sealed class DefaultSceneTests
{
    /// <summary>
    /// The default scene keeps the grid floor but leaves the world-origin axes out: the orientation gizmo answers
    /// "which way is X/Y/Z" without an occludable axes set sitting at the robot's feet. Turning the switch on adds
    /// them, already configured as a fixed-length world ruler.
    /// </summary>
    [Fact]
    public void DefaultScene_DoesNotAddWorldAxes_ButKeepsThemOneCallAway()
    {
        using var scene = new SceneGraph();

        Assert.DoesNotContain(scene.Roots, root => root is Axes);

        scene.ShowWorldAxes = true;
        Axes? axes = scene.AddDefaultWorldAxes();

        Assert.NotNull(axes);
        Assert.Equal(SceneGraph.WorldAxesName, axes!.Name);
        Assert.Equal(AxesSizing.FixedWorldLength, axes.Sizing);
        Assert.Equal(scene.WorldAxesLength, axes.Length, 3);
    }

    /// <summary>
    /// The orientation gizmo is on by default — it is the only orientation cue left once the world axes are off —
    /// and its screen-space knobs come with usable values.
    /// </summary>
    [Fact]
    public void DefaultScene_ShowsTheOrientationGizmo_InAPixelSizedSquare()
    {
        using var scene = new SceneGraph();

        Assert.True(scene.ShowOrientationGizmo);
        Assert.InRange(scene.OrientationGizmoSize, 96f, 256f);   // Big enough to read the axes at a glance.
        Assert.True(scene.OrientationGizmoMargin >= 0f);
    }

    /// <summary>
    /// A per-object marker wants a constant screen size: that is the default, so mounting local axes on a tiny or
    /// a huge link gives a mark of the same visual weight.
    /// </summary>
    [Fact]
    public void Axes_DefaultsToConstantScreenSize()
    {
        Assert.Equal(AxesSizing.ConstantScreenSize, new Axes().Sizing);
    }

    /// <summary>
    /// Fixed world length: <see cref="Axes.Length"/> is the drawn world length and stays writable after
    /// construction — the shader normalises by the arrow's own length, so re-scaling needs no geometry rebuild.
    /// </summary>
    [Fact]
    public void Axes_FixedWorldLength_ReportsAndUpdatesItsWorldLength()
    {
        var axes = new Axes(0.4f) { Sizing = AxesSizing.FixedWorldLength };

        Assert.Equal(0.4f, axes.Length, 3);

        axes.Length = 1.6f;
        Assert.Equal(1.6f, axes.Length, 3);

        Assert.Throws<ArgumentOutOfRangeException>(() => axes.Length = 0f);
    }

    /// <summary>
    /// Fitting: the axes end up longer than the content's largest extent (that is what makes them visible past the
    /// model instead of inside it), the created set takes the new length, and a second call converges instead of
    /// growing — the axes are not content themselves.
    /// </summary>
    [Fact]
    public void FitWorldAxesToContent_SizesTheAxesPastTheModel_AndConverges()
    {
        using var scene = new SceneGraph { ShowWorldAxes = true };
        scene.AddDefaultWorldAxes();
        scene.Add(new Box(2f, 3f, 1f));   // Largest world extent = 3 m along Y.

        float length = scene.FitWorldAxesToContent(1.15f);

        Assert.Equal(3f * 1.15f, length, 3);
        Assert.Equal(length, scene.WorldAxesLength, 3);
        Assert.Equal(length, Assert.Single(scene.Roots.OfType<Axes>()).Length, 3);

        Assert.Equal(length, scene.FitWorldAxesToContent(1.15f), 3);
    }

    /// <summary>A scene with nothing measurable in it keeps the length it has (no change, no exception).</summary>
    [Fact]
    public void FitWorldAxesToContent_WithoutContent_KeepsTheCurrentLength()
    {
        using var scene = new SceneGraph();
        float before = scene.WorldAxesLength;

        Assert.Equal(before, scene.FitWorldAxesToContent(), 3);
        Assert.Equal(before, scene.WorldAxesLength, 3);
    }

    /// <summary>A nonsense factor is rejected instead of silently producing a zero-length axes set.</summary>
    [Fact]
    public void FitWorldAxesToContent_RejectsANonPositiveFactor()
    {
        using var scene = new SceneGraph();

        Assert.Throws<ArgumentOutOfRangeException>(() => scene.FitWorldAxesToContent(0f));
    }

    /// <summary>
    /// Selection feedback is part of the default scene: a host that only calls <c>Select</c> gets both halves of it —
    /// the highlight and the picked node's own axes — and a fresh scene has nothing selected.
    /// </summary>
    [Fact]
    public void DefaultScene_ShowsSelectionAxes_AndStartsWithNothingSelected()
    {
        using var scene = new SceneGraph();

        Assert.True(scene.ShowSelectionAxes);
        Assert.True(scene.SelectionAxesLength > 0f);
        Assert.Null(scene.Selected);
    }

    /// <summary>
    /// Single selection end to end: the newly picked object takes the highlight and its own axes, the previous one
    /// loses both, and null just clears — the whole click path a host needs, in one call. The mounted axes are a
    /// display aid, so they are not pickable and cannot swallow the next click.
    /// </summary>
    [Fact]
    public void Select_MountsTheObjectsOwnAxes_AndDropsThePreviousSelection()
    {
        using var scene = new SceneGraph();
        var first = new Box(0.2f, 0.2f, 0.2f, "first");
        var second = new Box(0.2f, 0.2f, 0.2f, "second");
        scene.Add(first);
        scene.Add(second);

        Assert.Same(first, scene.Select(first));
        Assert.Same(first, scene.Selected);
        Assert.True(first.Highlighted);
        Assert.True(first.ShowLocalAxes);
        Assert.False(first.LocalAxes!.Pickable);
        Assert.Equal(scene.SelectionAxesLength, first.LocalAxes.Length, 3);

        Assert.Same(second, scene.Select(second));
        Assert.False(first.Highlighted);
        Assert.False(first.ShowLocalAxes);
        Assert.True(second.ShowLocalAxes);

        Assert.Null(scene.Select(null));
        Assert.Null(scene.Selected);
        Assert.False(second.Highlighted);
        Assert.False(second.ShowLocalAxes);
    }

    /// <summary>A host that manages its own selection keeps its own marker: Select only unmounts the axes it mounted.</summary>
    [Fact]
    public void Select_LeavesTheAxesTheSceneTurnedOnItself()
    {
        using var scene = new SceneGraph();
        var node = new Box(0.2f, 0.2f, 0.2f, "self-marked") { ShowLocalAxes = true };
        scene.Add(node);

        scene.Select(node);
        scene.Select(null);

        Assert.True(node.ShowLocalAxes);
    }

    /// <summary>With the axes switch off, selecting is still single selection — highlight only, no marker.</summary>
    [Fact]
    public void Select_WithoutSelectionAxes_OnlyHighlights()
    {
        using var scene = new SceneGraph { ShowSelectionAxes = false };
        var node = new Box(0.2f, 0.2f, 0.2f, "plain");
        scene.Add(node);

        scene.Select(node);

        Assert.True(node.Highlighted);
        Assert.False(node.ShowLocalAxes);
        Assert.Null(node.LocalAxes);
    }

    /// <summary>An axes length that is not a positive number is rejected at the property, not later inside the node.</summary>
    [Fact]
    public void SelectionAxesLength_RejectsANonPositiveValue()
    {
        using var scene = new SceneGraph();

        Assert.Throws<ArgumentOutOfRangeException>(() => scene.SelectionAxesLength = 0f);
    }

    /// <summary>
    /// The whole click path: <see cref="SceneGraph.PickAndSelect"/> selects what the ray actually hits and clears the
    /// selection when the ray hits nothing.
    /// </summary>
    [Fact]
    public void PickAndSelect_SelectsTheHit_AndClearsTheSelectionOnAMiss()
    {
        using var scene = new SceneGraph();
        var target = new Box(1f, 1f, 1f, "target");
        scene.Add(target);

        GameObject? hit = scene.PickAndSelect(new Ray(new Vector3(5f, 0f, 0f), new Vector3(-1f, 0f, 0f)));

        Assert.Same(target, hit);
        Assert.Same(target, scene.Selected);
        Assert.True(target.ShowLocalAxes);

        GameObject? miss = scene.PickAndSelect(new Ray(new Vector3(5f, 0f, 5f), new Vector3(1f, 0f, 0f)));

        Assert.Null(miss);
        Assert.Null(scene.Selected);
        Assert.False(target.ShowLocalAxes);
    }
}
