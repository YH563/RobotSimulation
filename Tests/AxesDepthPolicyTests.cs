using RobotSimulation.Core.Scene;

namespace RobotSimulation.Tests;

/// <summary>
/// The depth policy of an axes set, in tests that need no GL context, because the policy is scene-side data the
/// renderer reads — the drawing itself belongs to its overlay pass (see <c>Renderer.DrawOverlayAxes</c> and the host
/// smoke runs). What is pinned here is the default a host inherits: a per-object frame marker is drawn on top of the
/// model it annotates, because that marker sits <em>inside</em> the mesh — depth-tested, it is half buried in the very
/// geometry it exists to explain — while a world-scale ruler stays occludable, because a ruler that shows through
/// what it measures lies about the scene.
/// </summary>
public class AxesDepthPolicyTests
{
    [Fact]
    public void AnAxesSetIsDepthTestedByDefault()
    {
        // Ordinary scene geometry until it says otherwise: the default has to fit the ruler reading, since an
        // author's own set is usually there to measure the scene rather than to annotate one node in it.
        var axes = new Axes(1f);

        Assert.False(axes.AlwaysOnTop);
    }

    [Fact]
    public void TheLocalAxesMarkerIsDrawnOnTop()
    {
        var node = new GameObject(name: "link");
        Assert.False(node.ShowLocalAxes);
        Assert.Null(node.LocalAxes);

        node.ShowLocalAxes = true;

        Axes marker = Assert.IsType<Axes>(node.LocalAxes);
        Assert.True(marker.AlwaysOnTop);

        // Still an ordinary child node otherwise: it follows the frame it belongs to, and it stays out of picking so
        // the next click hits the object rather than the marker.
        Assert.Same(node.Transform, marker.Transform.Parent);
        Assert.False(marker.Pickable);
        foreach (Transform child in marker.Transform.Children)
            Assert.False(child.Owner.Pickable);
    }

    [Fact]
    public void AnOptOutSurvivesHidingTheMarker()
    {
        var node = new GameObject(name: "link");
        node.ShowLocalAxes = true;
        Axes marker = Assert.IsType<Axes>(node.LocalAxes);

        // A host that wants its markers occludable flips the flag on the mounted set. The set is created once and
        // reused across mounts (the same object the length policy follows), so hiding and showing the marker must not
        // quietly re-arm the default over that choice.
        marker.AlwaysOnTop = false;
        node.ShowLocalAxes = false;
        node.ShowLocalAxes = true;

        Assert.Same(marker, node.LocalAxes);
        Assert.False(marker.AlwaysOnTop);
        Assert.Same(node.Transform, marker.Transform.Parent);
    }

    [Fact]
    public void SelectionMountsAnOnTopMarkerAndUnmountsIt()
    {
        var scene = new SceneGraph();
        var node = new GameObject(name: "link");
        scene.Add(node);

        scene.Select(node);

        Axes marker = Assert.IsType<Axes>(node.LocalAxes);
        Assert.True(marker.AlwaysOnTop);

        // Clearing the selection takes the marker off again — and leaves it otherwise ready for the next pick.
        scene.Select(null);

        Assert.False(node.ShowLocalAxes);
        Assert.Null(marker.Transform.Parent);
        Assert.True(marker.AlwaysOnTop);
    }

    [Fact]
    public void TheWorldRulerStaysDepthTested()
    {
        var scene = new SceneGraph { ShowWorldAxes = true };

        Axes world = Assert.IsType<Axes>(scene.AddDefaultWorldAxes());

        // Opt-in and occludable, exactly as its own documentation promises: this set is sized past the model with
        // FitWorldAxesToContent() so it can be seen *around* the geometry, not through it.
        Assert.False(world.AlwaysOnTop);
    }
}
