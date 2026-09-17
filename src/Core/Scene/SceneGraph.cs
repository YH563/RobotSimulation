using System;
using System.Collections.Generic;
using System.Numerics;
using RobotSimulation.Core.Geometry;

namespace RobotSimulation.Core.Scene;

/// <summary>
/// Scene-graph container: holds the scene's root object tree, the active camera, a set of lights, and
/// the scene's display defaults (background/ambient light/grid floor). The settings are exposed as
/// read-write properties on this class plus default assembly methods — there is no separate
/// "settings/defaults" class; the out-of-the-box values are the defaults below.
/// </summary>
public class SceneGraph : IDisposable
{
    private readonly List<GameObject> _roots = new();
    private readonly List<Light> _lights = new();

    /// <summary>Root nodes of the scene (for renderer traversal and external read-only access).</summary>
    public IReadOnlyList<GameObject> Roots => _roots;

    /// <summary>The scene's active camera (a built-in orbit camera by default; can be replaced entirely).</summary>
    public Camera Camera { get; set; } = new Camera();

    // ---- Scene display defaults (host/UI reads and writes these properties directly) ----

    /// <summary>Clear/background color (medium gray, the default "sky" base).</summary>
    public Vector4 BackgroundColor { get; set; } = new(0.20f, 0.22f, 0.25f, 1f);

    /// <summary>Ambient light (RGB intensity, 0-1). Provides the floor for shadowed faces so they do not go black.</summary>
    public Vector3 AmbientColor { get; set; } = new(0.30f, 0.32f, 0.36f);

    /// <summary>Whether to show the default grid floor (<see cref="AddDefaultGrid"/> creates it based on this).</summary>
    public bool ShowGrid { get; set; } = true;

    /// <summary>
    /// Whether to show the default world-origin axes (<see cref="AddDefaultWorldAxes"/> creates it based on
    /// this). Off by default: the world origin is not where a robot's interesting frames are, the set is a
    /// plain scene object that the model occludes, and the screen-space orientation gizmo already answers
    /// "which way is X/Y/Z" without occupying the scene. Turn it on — and size it with
    /// <see cref="WorldAxesLength"/> or <see cref="FitWorldAxesToContent"/> so it stands out past the model —
    /// when a world-scale ruler is what the scene needs.
    /// </summary>
    public bool ShowWorldAxes { get; set; } = false;

    /// <summary>
    /// World length of the default world-origin axes: used when <see cref="AddDefaultWorldAxes"/> creates them
    /// and re-applied to the set already in the scene by <see cref="FitWorldAxesToContent"/>. The default axes
    /// are <see cref="AxesSizing.FixedWorldLength"/>, so this is a length in meters, not a screen size.
    /// </summary>
    public float WorldAxesLength { get; set; } = 0.5f;

    /// <summary>
    /// Whether the renderer draws the screen-space orientation gizmo: a small axes set pinned to the
    /// viewport's bottom-right corner that follows the camera's orientation, like a 3D engine's view widget.
    /// It is drawn last, into its own small viewport, so it is never occluded by scene geometry, it keeps a
    /// constant pixel size at any zoom, and — being no scene node — it can never take part in picking.
    /// </summary>
    public bool ShowOrientationGizmo { get; set; } = true;

    /// <summary>
    /// Size (pixels) of the orientation gizmo's square viewport in the bottom-right corner. The renderer's
    /// orthographic view is 2.2× the arrows' length, so the square is all marker: at 160 px each axis is drawn
    /// ~72 px long — readable at a glance, with no padding wasted around it.
    /// </summary>
    public float OrientationGizmoSize { get; set; } = 160f;

    /// <summary>Gap (pixels) between the orientation gizmo and the viewport's bottom/right edges.</summary>
    public float OrientationGizmoMargin { get; set; } = 16f;

    /// <summary>
    /// Whether the selected object shows its own local axes (<see cref="GameObject.ShowLocalAxes"/>): a small marker
    /// at that node's origin, tilted with its frame. A highlight tells the user *what* was picked; the axes tell them
    /// which way that node's X/Y/Z point — the same question the corner gizmo answers for the world. On by default,
    /// so a host that only calls <see cref="Select"/> gets both.
    /// <para>
    /// Display only, never a handle: the marker is a plain child node with no drag affordance, and nothing in this
    /// class writes back to a node's transform. Reference frames can be read with no chance of an accidental edit —
    /// which is what an articulated model needs, because a link's pose belongs to its joint chain, not to a widget.
    /// </para>
    /// </summary>
    public bool ShowSelectionAxes { get; set; } = true;

    /// <summary>
    /// Length (meters) of the axes mounted on the selected object. It is read when they are mounted
    /// (<see cref="Select"/>), so a change applies to the next selection rather than the current one.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a positive finite number.</exception>
    public float SelectionAxesLength
    {
        get => _selectionAxesLength;
        set
        {
            if (!float.IsFinite(value) || value <= 0f)
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "Selection axes length must be a positive finite value.");
            _selectionAxesLength = value;
        }
    }

    private float _selectionAxesLength = 0.3f;

    /// <summary>
    /// The currently selected object (null when nothing is selected). Read-only: selection changes go through
    /// <see cref="Select"/> / <see cref="PickAndSelect"/>, which keep the highlight and the mounted axes in step.
    /// </summary>
    public GameObject? Selected { get; private set; }

    /// <summary>Cell edge length (meters).</summary>
    public float GridCellSize { get; set; } = 1f;

    /// <summary>Cell count from the center in each direction (total span = 2 × GridCellCount × GridCellSize).</summary>
    public int GridCellCount { get; set; } = 10;

    /// <summary>Grid line color.</summary>
    public Vector4 GridColor { get; set; } = new(0.30f, 0.30f, 0.35f, 1f);

    /// <summary>All lights in the scene (collected automatically by <see cref="Add"/>).</summary>
    public IReadOnlyList<Light> Lights => _lights;

    private bool _disposed;

    /// <summary>Whether <see cref="Select"/> mounted the current selection's axes, so only those are unmounted again.</summary>
    private bool _selectionAxesMounted;

    /// <summary>
    /// Construction completes the default scene assembly: default camera pose, default lights, grid floor,
    /// and the world-origin axes when <see cref="ShowWorldAxes"/> is on (it is off by default — the renderer's
    /// orientation gizmo covers the "which way is up" question without putting geometry in the scene). The
    /// host does not need to call these manually (it only adds its own content, such as a URDF robot or demo
    /// objects).
    /// </summary>
    public SceneGraph()
    {
        ApplyDefaultCamera();
        AddDefaultLights();
        AddDefaultGrid();
        AddDefaultWorldAxes();
    }

    /// <summary>
    /// Adds an object (auto-detecting whether it is a root node). If <paramref name="obj"/> is a
    /// <see cref="Light"/> and not already registered, it is also added to <see cref="Lights"/>.
    /// </summary>
    public void Add(GameObject? obj)
    {
        if (obj == null) throw new ArgumentNullException(nameof(obj));
        if (obj.Transform.Parent == null)
            _roots.Add(obj);

        if (obj is Light light && !_lights.Contains(light))
            _lights.Add(light);
    }

    /// <summary>
    /// Removes a game object, detaching it from its parent or the root list; if it is a <see cref="Light"/>
    /// it is also removed from the lights list.
    /// </summary>
    public void Remove(GameObject? obj)
    {
        if (obj == null) return;

        if (obj is Light light)
            _lights.Remove(light);

        if (obj.Transform.Parent == null)
        {
            // If it is a root node, remove it from the root list.
            _roots.Remove(obj);
        }
        else
        {
            // If it is not a root, set Parent to null, which auto-detaches it from its parent's Children.
            obj.Transform.Parent = null;
        }
    }

    // ------------------------------------------------------------------
    // Default scene assembly (grid / lights / initial camera pose; no separate "settings class")
    // ------------------------------------------------------------------

    /// <summary>Creates and adds the grid floor from the current grid properties; does not create it when <see cref="ShowGrid"/> is off.</summary>
    public Grid? AddDefaultGrid()
    {
        if (!ShowGrid)
            return null;

        var grid = new Grid(GridCellSize, GridCellCount, GridColor);
        grid.SetSubtreePickable(false);   // The grid is a reference plane and should not participate in picking.
        Add(grid);
        return grid;
    }

    /// <summary>Adds the default key light plus a fill light.</summary>
    public void AddDefaultLights()
    {
        var key = new Light("key_light") { Color = Vector3.One, Intensity = 0.85f };
        key.Transform.Position = new Vector3(4f, 5f, 10f);   // Z-up: place it above the ground.
        Add(key);

        var fill = new Light("fill_light") { Color = new Vector3(0.55f, 0.55f, 0.65f), Intensity = 0.4f };
        fill.Transform.Position = new Vector3(-5f, -3f, 6f);
        Add(fill);
    }

    /// <summary>Name of the default world-origin axes node (how a re-fit finds the set already in the scene).</summary>
    public const string WorldAxesName = "world-axes";

    /// <summary>
    /// Creates and adds the default global axes at the world origin based on <see cref="ShowWorldAxes"/>; does
    /// not create when off. The set is <see cref="AxesSizing.FixedWorldLength"/> with
    /// <see cref="WorldAxesLength"/> meters per axis, so it measures the scene instead of tracking the zoom.
    /// </summary>
    public Axes? AddDefaultWorldAxes()
    {
        if (!ShowWorldAxes)
            return null;

        var axes = new Axes(WorldAxesLength, name: WorldAxesName)
        {
            Transform = { Position = Vector3.Zero },
            Sizing = AxesSizing.FixedWorldLength,
        };
        axes.SetSubtreePickable(false);   // The axes are a display aid and should not participate in picking.
        Add(axes);
        return axes;
    }

    /// <summary>
    /// Sizes the world axes to the scene's content, so a reference axes set stands out beyond the model instead
    /// of hiding inside it: <see cref="WorldAxesLength"/> becomes the content's largest world extent ×
    /// <paramref name="factor"/>, and the world axes already in the scene (if any) take that length as well.
    /// Display aids (the axes themselves, the grid, lights) are ignored while measuring, so calling this twice
    /// converges instead of growing. Returns the applied length; with nothing measurable in the scene the
    /// current <see cref="WorldAxesLength"/> is kept and returned.
    /// </summary>
    /// <param name="factor">Multiplier on the measured extent (&gt;1 puts the axes past the content).</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="factor"/> is not a positive finite number.</exception>
    public float FitWorldAxesToContent(float factor = 1.15f)
    {
        if (!float.IsFinite(factor) || factor <= 0f)
            throw new ArgumentOutOfRangeException(nameof(factor), factor, "Factor must be a positive finite value.");

        float extent = MeasureContentExtent();
        if (extent <= 0f)
            return WorldAxesLength;

        WorldAxesLength = extent * factor;

        // A set created by an earlier AddDefaultWorldAxes keeps its own copy of the length; push the new value
        // into it too, so the call works whether it comes before or after creation.
        foreach (GameObject root in _roots)
        {
            if (root is Axes { Name: WorldAxesName } axes)
                axes.Length = WorldAxesLength;
        }

        return WorldAxesLength;
    }

    /// <summary>
    /// Largest world-space extent of the scene's drawable content (the biggest side of the combined AABB), or 0
    /// when nothing measurable is in the scene. Axes sets, the grid and lights are skipped: they are display
    /// aids, and including the axes would make them grow with themselves on every call.
    /// </summary>
    private float MeasureContentExtent()
    {
        Vector3 min = new(float.PositiveInfinity);
        Vector3 max = new(float.NegativeInfinity);

        foreach (GameObject root in _roots)
            MeasureRecursive(root, ref min, ref max);

        if (min.X > max.X)   // Nothing measurable was visited.
            return 0f;

        Vector3 size = max - min;
        return MathF.Max(size.X, MathF.Max(size.Y, size.Z));
    }

    /// <summary>Accumulates a node subtree's world-space AABB corners into <paramref name="min"/>/<paramref name="max"/>.</summary>
    private static void MeasureRecursive(GameObject node, ref Vector3 min, ref Vector3 max)
    {
        if (node is Axes)
            return;   // An axes set describes the world, it is not part of the world's content.

        if (node.MeshData is { } mesh)
        {
            Bounds bounds = mesh.ComputeBounds();
            Matrix4x4 model = node.Transform.GetModelMatrix();
            for (int corner = 0; corner < 8; corner++)
            {
                var local = new Vector3(
                    (corner & 1) == 0 ? bounds.Min.X : bounds.Max.X,
                    (corner & 2) == 0 ? bounds.Min.Y : bounds.Max.Y,
                    (corner & 4) == 0 ? bounds.Min.Z : bounds.Max.Z);
                Vector3 world = Vector3.Transform(local, model);
                min = Vector3.Min(min, world);
                max = Vector3.Max(max, world);
            }
        }

        foreach (Transform child in node.Transform.Children)
            MeasureRecursive(child.Owner, ref min, ref max);
    }

    /// <summary>Poses the camera to the default initial pose (viewing the scene up close).</summary>
    public void ApplyDefaultCamera()
    {
        Camera.Target = new Vector3(0f, 0f, 0.3f);
        Camera.Distance = 3.2f;
        Camera.Pitch = 25f;
        Camera.Yaw = -90f;
    }

    /// <summary>
    /// Ray-picks all "visible, pickable, mesh-carrying" nodes in the scene and returns the nearest hit,
    /// or null if none. By default it only picks objects where <see cref="GameObject.Visible"/> and
    /// <see cref="GameObject.Pickable"/> are both true and <see cref="GameObject.MeshData"/> is non-null;
    /// <see cref="LineData"/>-based nodes (grid floor, axes) and point-cloud
    /// <see cref="PointCloud2Data"/> carry no lighting and are skipped during picking.
    /// </summary>
    /// <param name="ray">A world-space ray (ideally from <see cref="Camera.ScreenToWorldRay"/>).</param>
    /// <param name="predicate">Optional filter (e.g. only a certain kind/link); return true to participate.</param>
    /// <param name="hitInvisible">When true, also hit invisible objects.</param>
    public RaycastHit? Pick(Ray ray, Func<GameObject, bool>? predicate = null, bool hitInvisible = false)
    {
        RaycastHit? best = null;
        foreach (var root in _roots)
            PickRecursive(root, ray, predicate, hitInvisible, ref best);
        return best;
    }

    /// <summary>
    /// Pick-and-highlight closed loop: runs <see cref="Pick"/> on <paramref name="ray"/>, and on a hit
    /// sets that object's <see cref="GameObject.Highlighted"/> to <paramref name="enable"/> (default true)
    /// and returns it; returns null on a miss. A pure forwarding helper — it only sets highlight data and
    /// does not own the host's input framework (e.g. mouse event polling).
    /// </summary>
    /// <param name="ray">The ray to test (world coordinates, ideally from <see cref="Camera.ScreenToWorldRay"/>).</param>
    /// <param name="enable">Whether to set <see cref="GameObject.Highlighted"/> true/false on a hit.</param>
    /// <param name="predicate">Optional filter: objects returning false do not participate (e.g. only respond to robot nodes).</param>
    /// <param name="hitInvisible">When true, also hit invisible objects.</param>
    /// <returns>The hit object, or null if no ray/node was hit.</returns>
    public GameObject? PickAndHighlight(Ray ray, bool enable = true,
        Func<GameObject, bool>? predicate = null, bool hitInvisible = false)
    {
        RaycastHit? hit = Pick(ray, predicate, hitInvisible);
        if (hit is not { } found)
            return null;

        found.Object.Highlighted = enable;
        return found.Object;
    }

    /// <summary>
    /// Single-selection entry point: the new object becomes <see cref="Selected"/>, the previous one returns to its
    /// plain look, and — with <see cref="ShowSelectionAxes"/> on — the new object gets its local axes mounted as an
    /// ordinary, non-pickable child node (<see cref="GameObject.ShowLocalAxes"/>), so a later click still hits the
    /// object instead of its marker. That marker is read-only by design: it shows a frame, it never moves one, and
    /// selecting is never a manipulation mode — a link's pose stays the business of its joint chain. Passing null
    /// clears the selection. A host that owns its own selection state can keep calling <see cref="PickAndHighlight"/>,
    /// which only flips the highlight.
    /// </summary>
    /// <param name="obj">The object to select, or null to clear the selection.</param>
    /// <param name="highlight">Whether the newly selected object takes the highlight color (default true).</param>
    /// <returns>The selected object (the same <paramref name="obj"/>), or null when the selection was cleared.</returns>
    public GameObject? Select(GameObject? obj, bool highlight = true)
    {
        if (ReferenceEquals(obj, Selected))
            return Selected;

        if (Selected is { } previous)
        {
            previous.Highlighted = false;

            // Unmount only what Select mounted: local axes the scene's author turned on deliberately stay on.
            if (_selectionAxesMounted)
            {
                previous.ShowLocalAxes = false;
                _selectionAxesMounted = false;
            }
        }

        Selected = obj;
        if (obj is null)
            return null;

        obj.Highlighted = highlight;

        if (ShowSelectionAxes && !obj.ShowLocalAxes)
        {
            obj.LocalAxesLength = SelectionAxesLength;
            obj.ShowLocalAxes = true;
            if (obj.LocalAxes is { } mounted)
                mounted.Length = SelectionAxesLength;   // Axes created by an earlier selection take the new length.
            _selectionAxesMounted = true;
        }

        return obj;
    }

    /// <summary>
    /// Pick-and-select closed loop: runs <see cref="Pick"/> on <paramref name="ray"/> and feeds the hit — or null on a
    /// miss — through <see cref="Select"/>. One call is a host's whole click path: highlight what was picked, mount
    /// its axes, drop the previous selection.
    /// </summary>
    /// <param name="ray">The ray to test (world coordinates, ideally from <see cref="Camera.ScreenToWorldRay"/>).</param>
    /// <param name="predicate">Optional filter: objects returning false do not participate.</param>
    /// <param name="hitInvisible">When true, also hit invisible objects.</param>
    /// <returns>The object the ray selected, or null when it hit nothing (the selection is cleared).</returns>
    public GameObject? PickAndSelect(Ray ray, Func<GameObject, bool>? predicate = null, bool hitInvisible = false)
        => Select(Pick(ray, predicate, hitInvisible)?.Object);

    private void PickRecursive(GameObject node, in Ray ray, Func<GameObject, bool>? predicate,
        bool hitInvisible, ref RaycastHit? best)
    {
        if (node.Pickable && (hitInvisible || node.Visible)
            && node.MeshData is { } mesh && mesh.TriangleCount > 0
            && (predicate?.Invoke(node) ?? true))
        {
            TryPickMesh(node, mesh, ray, ref best);
        }

        foreach (var child in node.Transform.Children)
            PickRecursive(child.Owner, ray, predicate, hitInvisible, ref best);
    }

    private void TryPickMesh(GameObject node, MeshData mesh, in Ray ray, ref RaycastHit? best)
    {
        Matrix4x4 model = node.Transform.GetModelMatrix();
        if (!Matrix4x4.Invert(model, out Matrix4x4 invModel))
            return;   // Non-invertible model matrix (e.g. scale=0): ignore.

        // Transform the ray into the node's local space: broad-phase against the local AABB, then
        // per-triangle exact hits.
        Vector3 localOrigin = Vector3.Transform(ray.Origin, invModel);
        Vector3 localDir = Vector3.Normalize(Vector3.TransformNormal(ray.Direction, invModel));
        var localRay = new Ray(localOrigin, localDir);

        if (Raycast.HitAABB(localRay, mesh.ComputeBounds()) is null)
            return;

        IReadOnlyList<Vector3> pos = mesh.Positions;
        IReadOnlyList<uint> idx = mesh.Indices;

        float bestLocalDist = float.MaxValue;
        Vector3 bestLocalPoint = default, bestLocalNormal = default;
        float bestU = 0f, bestV = 0f;

        for (int i = 0; i < idx.Count; i += 3)
        {
            Vector3 a = pos[(int)idx[i]];
            Vector3 b = pos[(int)idx[i + 1]];
            Vector3 c = pos[(int)idx[i + 2]];

            if (Raycast.HitTriangle(localRay, a, b, c, out float t, out Vector3 n, out float u, out float v)
                && t < bestLocalDist)
            {
                bestLocalDist = t;
                bestLocalPoint = localOrigin + localDir * t;
                bestLocalNormal = n;
                bestU = u;
                bestV = v;
            }
        }

        if (bestLocalDist >= float.MaxValue)
            return;

        // Transform the local hit point/normal back to world; world distance is measured from the world
        // hit point to the ray origin (under non-uniform scaling local t ≠ world distance).
        Vector3 worldPoint = Vector3.Transform(bestLocalPoint, model);
        Vector3 worldNormal = Vector3.Normalize(Vector3.TransformNormal(bestLocalNormal, model));
        float distance = (worldPoint - ray.Origin).Length();

        if (best is null || distance < best.Value.Distance)
            best = new RaycastHit(node, worldPoint, distance, worldNormal, bestU, bestV);
    }

    /// <summary>
    /// Recursively updates all nodes (called from a background thread; never manipulate OpenGL here).
    /// </summary>
    public void Update(double deltaTime)
    {
        foreach (var root in _roots)
            UpdateRecursive(root, deltaTime);
    }

    private void UpdateRecursive(GameObject node, double deltaTime)
    {
        node.Update(deltaTime);
        foreach (var child in node.Transform.Children)
            UpdateRecursive(child.Owner, deltaTime);
    }

    /// <summary>
    /// Drops the scene's node and light lists. GPU resources are owned by the renderer
    /// (<c>RobotSimulation.OpenGL.Renderer</c>), so nothing here touches OpenGL; the call is idempotent.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;

        // GPU meshes/materials are released by the renderer (Renderer); the scene only holds data
        // references, so here we just clear the node lists.
        _roots.Clear();
        _lights.Clear();
        Selected = null;   // The node list is gone: a stale selection would point into nothing.
        _selectionAxesMounted = false;
        _disposed = true;
    }
}
