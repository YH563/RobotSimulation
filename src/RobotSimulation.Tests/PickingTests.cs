using System;
using System.Collections.Generic;
using System.Numerics;
using RobotSimulation.Core.Geometry;
using RobotSimulation.Core.Scene;
using RobotSimulation.Core.Utils;
using RobotSimulation.Robot;
using Xunit;

namespace RobotSimulation.Tests;

/// <summary>
/// Ray picking end to end and headless: the chain a host drives on a click, run against the real URDF
/// robot both hosts load.
/// <para>
/// Picking is only ever as correct as the correspondence between the frame the user is looking at and the
/// ray the host casts. These checks pin that correspondence down from both ends:
/// <list type="bullet">
/// <item>a surface point projected to a pixel must unproject back to a ray that picks the very link the
/// point belongs to, and the reported hit point must project back onto that same pixel;</item>
/// <item>a click has to be resolved against the camera pose that was live when the button went down: the
/// pointer drift a host still tolerates inside one click (±5px) has already rotated the camera, which is
/// enough to move the pick off a thin link — and because the drift is a rotation while the sensitivity is
/// per pointer pixel and the projection is per window pixel, it covers more of the picture on a larger
/// window. That is the difference a host has to make up for by restoring the press-time pose.</item>
/// </list>
/// </para>
/// </summary>
public sealed class PickingTests
{
    /// <summary>Pointer drift in device pixels that a host still calls "a click" (see the hosts' threshold).</summary>
    private const float ClickJitterPixels = 5f;

    /// <summary>rvis-style rotate sensitivity shared by both hosts: degrees per pointer pixel.</summary>
    private const float RotateDegreesPerPixel = 0.2f;

    /// <summary>How many surface points per link are sampled (closest to the camera first).</summary>
    private const int SamplesPerNode = 5;

    /// <summary>A point on one link's surface, already projected to the pixel the user would click.</summary>
    private readonly record struct SurfaceSample(GameObject Node, Vector3 Point, Vector2 Pixel, float Distance);

    [Theory]
    // Wide, square-ish and tall viewports: the aspect ratio feeds the projection the ray is built from.
    [InlineData(1100f, 693f)]
    [InlineData(800f, 800f)]
    [InlineData(1600f, 900f)]
    public void Pick_SurfacePointsProjectedToTheScreen_SelectTheLinkTheyBelongTo(float width, float height)
    {
        var viewport = new Vector2(width, height);
        using SceneGraph scene = CreateScene(out _);
        scene.Camera.AspectRatio = viewport.X / viewport.Y;

        List<SurfaceSample> samples = CollectSurfaceSamples(scene, viewport);
        Assert.NotEmpty(samples);

        int picksThatReachedTheirSample = 0;
        foreach (SurfaceSample sample in samples)
        {
            Ray ray = scene.Camera.ScreenToWorldRay(sample.Pixel, viewport);
            RaycastHit? hit = scene.Pick(ray);

            // The ray goes through a surface point of a visible link, so something has to be hit.
            Assert.NotNull(hit);

            // The hit point reported back must sit on the pixel that was clicked: this is the
            // "the ray matches the picture" half of the correspondence.
            Vector2 reported = ProjectToPixel(hit.Value.Point, scene.Camera, viewport);
            Assert.True(Vector2.Distance(reported, sample.Pixel) < 0.5f,
                $"Hit point {hit.Value.Point} was reported for pixel {sample.Pixel} but projects back to {reported}.");

            // A nearer link may legitimately cover this point (the sample set is not a visibility test);
            // what may never happen is that the ray arrives at the sample's own distance and still
            // reports a different object — that is a click that lands "next to" the part.
            if (hit.Value.Distance < sample.Distance - 1e-3f)
                continue;

            picksThatReachedTheirSample++;
            Assert.Same(sample.Node, hit.Value.Object);
        }

        Assert.True(picksThatReachedTheirSample > 0,
            "No sampled surface point survived to its own depth: the sample set never sees a link first.");
    }

    [Fact]
    public void Pick_AfterAClickSizedPointerNudge_LandsOffThePointTheUserClicked()
    {
        var viewport = new Vector2(1100f, 693f);
        using SceneGraph scene = CreateScene(out _);
        scene.Camera.AspectRatio = viewport.X / viewport.Y;

        SurfaceSample sample = FindSampleUnderTheCursor(scene, viewport);
        RaycastHit before = PickAt(scene, sample.Pixel, viewport)!.Value;
        Assert.Same(sample.Node, before.Object);

        // The pointer drift a host still tolerates inside one click (5px at 0.2°/px = 1°) has already been
        // applied to the camera by the time the release is classified as a pick: the ray is then cast through
        // a pose the user never looked through, so the pixel no longer resolves to the point that was clicked.
        CameraPose atPress = CameraPose.Capture(scene.Camera);
        scene.Camera.Rotate(-ClickJitterPixels * RotateDegreesPerPixel, 0f);

        RaycastHit afterDrift = PickAt(scene, sample.Pixel, viewport)!.Value;
        float moved = Vector3.Distance(afterDrift.Point, before.Point);
        Assert.True(moved > 1e-4f,
            $"A {ClickJitterPixels}px drift did not move the pick target at all " +
            $"({before.Point} → {afterDrift.Point}); the check no longer exercises the bug it guards.");

        // The contract the hosts implement: a click restores the pose captured at press time, so the very same
        // pixel resolves to exactly the point — and the same link — it did when the button went down.
        atPress.Restore(scene.Camera);
        RaycastHit restored = PickAt(scene, sample.Pixel, viewport)!.Value;
        Assert.Same(sample.Node, restored.Object);
        Assert.Equal(before.Point, restored.Point);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void ScreenToWorldRay_PointerAndViewportScaledTogether_YieldTheSameRay(double scaling)
    {
        // What a host has to do on a scaled display: convert the pointer to device pixels and hand the ray the
        // device-pixel viewport it rendered into — either both, or neither. Scaling only one of them is the
        // classic embedding bug: the ray is then tilted by a fraction of the control, so clicks land beside
        // the link and the error grows with the window. Scaling both has to leave the ray where it was.
        var layoutViewport = new Vector2(1100f, 693f);
        var pointer = new Vector2(548f, 413f);

        using SceneGraph scene = CreateScene(out _);
        scene.Camera.AspectRatio = layoutViewport.X / layoutViewport.Y;
        Ray atLayoutScale = scene.Camera.ScreenToWorldRay(pointer, layoutViewport);

        // The hosts' own conversion: layout units × scale factor, viewport rounded up to whole device pixels.
        var deviceViewport = new Vector2(
            (float)Math.Ceiling(layoutViewport.X * scaling),
            (float)Math.Ceiling(layoutViewport.Y * scaling));
        scene.Camera.AspectRatio = deviceViewport.X / deviceViewport.Y;
        Ray atDeviceScale = scene.Camera.ScreenToWorldRay(pointer * (float)scaling, deviceViewport);

        Assert.Equal(atLayoutScale.Origin, atDeviceScale.Origin);

        float angleDegrees = MathUtils.RadiansToDegrees(
            MathF.Acos(Math.Clamp(Vector3.Dot(atLayoutScale.Direction, atDeviceScale.Direction), -1f, 1f)));
        Assert.True(angleDegrees < 0.05f,
            $"Scaling the pointer and the viewport together by {scaling} turned the ray by {angleDegrees:F4}°: " +
            "the world point under the cursor would move.");
    }

    [Fact]
    public void Pick_ClickDriftCoversMorePixelsOnALargerWindow()
    {
        float onSmallWindow = ClickDriftInScreenPixels(800f, 600f);
        float onLargeWindow = ClickDriftInScreenPixels(1600f, 1200f);

        // The drift is a camera rotation while the sensitivity is per pointer pixel and the projection is per
        // window pixel, so the same hand jitter covers twice as much of the picture on twice the window. That
        // is what turns "a click that usually works" into "a click that sometimes selects nothing" after the
        // window is enlarged — the drift grows while the link keeps its pixel size.
        Assert.True(onSmallWindow > 0.5f, $"The drift is not observable at all ({onSmallWindow:F2}px).");
        Assert.True(onLargeWindow > onSmallWindow * 1.5f,
            $"The drift moved {onLargeWindow:F2}px on 1600x1200 against {onSmallWindow:F2}px on 800x600: " +
            "it no longer scales with the window.");
    }

    [Fact]
    public void Pick_CompositorSurfaceLargerThanTheLayoutPrediction_ClickStillLandsOnTheLink()
    {
        // Measured on a real Avalonia 12 window (X11, no display scaling): the host logged
        // "Viewport: 1157x755 px from framebuffer | control layout 1100x718 at scaling 1.00" — the surface the
        // compositor hands the control is 5.2% larger than Bounds × RenderScaling, and the compositor scales
        // that whole surface onto the control's layout rectangle. A host that draws into a layout-sized
        // viewport inside that surface *and* converts the pointer with RenderScaling therefore casts the ray
        // against a rectangle ~5% smaller than the picture the user is looking at: away from the centre the
        // click lands further and further beside the link, and the offset grows with the window — the
        // "clicks miss once the window is enlarged" report, in numbers.
        var layoutSize = new Vector2(1100f, 718f);
        var surfaceSize = new Vector2(1157f, 755f);
        var pixelsPerLayoutUnit = new Vector2(surfaceSize.X / layoutSize.X, surfaceSize.Y / layoutSize.Y);

        using SceneGraph scene = CreateScene(out _);

        // The picture as the host drew it wrong: into a layout-sized viewport (aspect from the layout).
        scene.Camera.AspectRatio = layoutSize.X / layoutSize.Y;
        List<SurfaceSample> samples = CollectSurfaceSamples(scene, layoutSize);
        Assert.NotEmpty(samples);

        // Where the user's eye is on a point of the picture, given the viewport it was rendered with: the
        // point is drawn at a texel inside the surface, and the compositor scales the whole surface onto the
        // control's layout rectangle — that position, in the control's own coordinates, is what the user aims
        // the cursor at.
        Vector2 CursorAt(SurfaceSample sample, Vector2 renderViewport) =>
            ProjectToPixel(sample.Point, scene.Camera, renderViewport) / pixelsPerLayoutUnit;

        // The error a wrong viewport introduces grows with the distance from the centre of the picture, so the
        // sample furthest from it is the click a user notices the miss on first.
        SurfaceSample edge = samples
            .OrderByDescending(sample => Vector2.Distance(sample.Pixel, layoutSize * 0.5f))
            .First();
        Ray predicted = scene.Camera.ScreenToWorldRay(CursorAt(edge, layoutSize), layoutSize);
        float drift = DistanceFromRayTo(predicted, edge.Point);

        // Measured with the numbers above: 148mm at the edge of this scene, more than twice the diameter of a
        // link of the test robot — which is why a click near the silhouette selects nothing.
        Assert.True(drift > 0.05f,
            $"The layout prediction misses the point under the cursor by only {drift * 1000f:F1}mm; the check " +
            "no longer reproduces the embedding bug it guards.");

        // The fix: the picture is drawn into the measured surface and the pointer crosses the measured ratio,
        // so the ray goes through the very point the user is looking at — and for the samples the camera
        // really does see first, the pick then selects their own link.
        scene.Camera.AspectRatio = surfaceSize.X / surfaceSize.Y;
        int reachedTheirLink = 0;
        float worstRoundTrip = 0f;
        foreach (SurfaceSample sample in samples)
        {
            Vector2 pixel = CursorAt(sample, surfaceSize) * pixelsPerLayoutUnit;
            RaycastHit? hit = scene.Pick(scene.Camera.ScreenToWorldRay(pixel, surfaceSize));
            if (hit is not { } found)
                continue;

            worstRoundTrip = MathF.Max(worstRoundTrip,
                Vector2.Distance(ProjectToPixel(found.Point, scene.Camera, surfaceSize), pixel));

            if (ReferenceEquals(found.Object, sample.Node))
                reachedTheirLink++;
        }

        Assert.True(worstRoundTrip < 0.5f,
            $"The measured conversion reports hits up to {worstRoundTrip:F2}px away from the pixel that was asked about.");
        Assert.True(reachedTheirLink > 0,
            "The measured conversion selects no sampled link at all: the ray is not where the cursor is.");
    }

    /// <summary>
    /// How far the link furthest from the screen centre travels across the viewport when the camera is rotated
    /// by the pointer drift of one click — the click error a host inherits from a camera that followed the
    /// drift instead of holding the pose the button was pressed in.
    /// </summary>
    private static float ClickDriftInScreenPixels(float width, float height)
    {
        var viewport = new Vector2(width, height);
        using SceneGraph scene = CreateScene(out _);
        scene.Camera.AspectRatio = viewport.X / viewport.Y;

        // The point furthest from the centre is the one a small camera rotation displaces the most.
        var samples = CollectSurfaceSamples(scene, viewport);
        Assert.NotEmpty(samples);
        Vector3 world = samples
            .OrderByDescending(sample => Vector2.Distance(sample.Pixel, viewport * 0.5f))
            .First().Point;

        Vector2 before = ProjectToPixel(world, scene.Camera, viewport);
        scene.Camera.Rotate(-ClickJitterPixels * RotateDegreesPerPixel, 0f);
        return Vector2.Distance(before, ProjectToPixel(world, scene.Camera, viewport));
    }

    /// <summary>The host's click path: screen pixel → world ray → nearest hit (null on a miss).</summary>
    private static RaycastHit? PickAt(SceneGraph scene, Vector2 pixel, Vector2 viewport)
        => scene.Pick(scene.Camera.ScreenToWorldRay(pixel, viewport));

    /// <summary>The default scene (grid, lights, camera; world axes are opt-in) plus the robot both hosts load.</summary>
    private static SceneGraph CreateScene(out RobotModel robot)
    {
        var scene = new SceneGraph();
        robot = RobotModel.ParseFile(TestAssets.Model("fairino3_v6/fairino3_v6.urdf"));
        scene.Add(robot);
        return scene;
    }

    /// <summary>
    /// Samples each pickable mesh's surface: front-facing triangles only (so the camera can see them),
    /// projected inside the viewport, nearest to the camera first.
    /// </summary>
    private static List<SurfaceSample> CollectSurfaceSamples(SceneGraph scene, Vector2 viewport)
    {
        var samples = new List<SurfaceSample>();
        Vector3 eye = scene.Camera.Position;

        foreach (GameObject node in EnumeratePickable(scene.Roots))
        {
            MeshData mesh = node.MeshData!;
            Matrix4x4 model = node.Transform.GetModelMatrix();
            var candidates = new List<(float Distance, Vector3 Point, Vector2 Pixel)>();

            // A stride keeps the scan proportional to the sample count, not to the mesh size.
            int stride = Math.Max(1, mesh.TriangleCount / 4000);
            for (int i = 0; i + 2 < mesh.Indices.Count; i += 3 * stride)
            {
                Vector3 a = Vector3.Transform(mesh.Positions[(int)mesh.Indices[i]], model);
                Vector3 b = Vector3.Transform(mesh.Positions[(int)mesh.Indices[i + 1]], model);
                Vector3 c = Vector3.Transform(mesh.Positions[(int)mesh.Indices[i + 2]], model);

                Vector3 center = (a + b + c) / 3f;
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), center - eye) >= 0f)
                    continue; // Back-facing: the camera cannot see this triangle.

                Vector2 pixel = ProjectToPixel(center, scene.Camera, viewport);
                if (pixel.X < 1f || pixel.Y < 1f || pixel.X > viewport.X - 1f || pixel.Y > viewport.Y - 1f)
                    continue; // Off-screen.

                candidates.Add((Vector3.Distance(center, eye), center, pixel));
            }

            candidates.Sort((left, right) => left.Distance.CompareTo(right.Distance));
            for (int i = 0; i < Math.Min(SamplesPerNode, candidates.Count); i++)
                samples.Add(new SurfaceSample(node, candidates[i].Point, candidates[i].Pixel, candidates[i].Distance));
        }

        return samples;
    }

    /// <summary>A sample the camera really does see first (picking that pixel selects its own link).</summary>
    private static SurfaceSample FindSampleUnderTheCursor(SceneGraph scene, Vector2 viewport)
    {
        foreach (SurfaceSample sample in CollectSurfaceSamples(scene, viewport))
        {
            RaycastHit? hit = scene.Pick(scene.Camera.ScreenToWorldRay(sample.Pixel, viewport));
            if (hit is { } found && ReferenceEquals(found.Object, sample.Node))
                return sample;
        }

        Assert.Fail("No sampled surface point is the first hit of its own pixel.");
        return default;
    }

    /// <summary>World point → pixel (top-left origin), the exact inverse of <see cref="Camera.ScreenToWorldRay"/>.</summary>
    private static Vector2 ProjectToPixel(Vector3 world, Camera camera, Vector2 viewport)
    {
        Matrix4x4 viewProjection = camera.GetViewMatrix() * camera.GetProjectionMatrix();
        Vector4 clip = Vector4.Transform(new Vector4(world, 1f), viewProjection);
        float inverseW = 1f / clip.W;

        return new Vector2(
            (clip.X * inverseW + 1f) * 0.5f * viewport.X,
            (1f - clip.Y * inverseW) * 0.5f * viewport.Y);
    }

    /// <summary>Distance from a world point to a ray: 0 when the point lies on it (ray directions are unit length).</summary>
    private static float DistanceFromRayTo(Ray ray, Vector3 point)
    {
        Vector3 toPoint = point - ray.Origin;
        Vector3 closest = ray.Origin + Vector3.Dot(toPoint, ray.Direction) * ray.Direction;
        return Vector3.Distance(closest, point);
    }

    private static IEnumerable<GameObject> EnumeratePickable(IReadOnlyList<GameObject> roots)
    {
        foreach (GameObject root in roots)
        {
            foreach (GameObject node in EnumerateSubtree(root))
            {
                if (node.Pickable && node.Visible && node.MeshData is { TriangleCount: > 0 })
                    yield return node;
            }
        }
    }

    private static IEnumerable<GameObject> EnumerateSubtree(GameObject node)
    {
        yield return node;
        foreach (Transform child in node.Transform.Children)
        {
            foreach (GameObject descendant in EnumerateSubtree(child.Owner))
                yield return descendant;
        }
    }

    /// <summary>
    /// The orbit state a host has to keep frozen across a click: capturing it at press time and restoring
    /// it before picking is what "a click does not move the camera" means in code.
    /// </summary>
    private readonly record struct CameraPose(float Yaw, float Pitch, float Distance, Vector3 Target)
    {
        public static CameraPose Capture(Camera camera)
            => new(camera.Yaw, camera.Pitch, camera.Distance, camera.Target);

        public void Restore(Camera camera)
        {
            camera.Target = Target;
            camera.Distance = Distance;
            camera.Yaw = Yaw;
            camera.Pitch = Pitch;
        }
    }
}
