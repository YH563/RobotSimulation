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

    /// <summary>Whether to show the default world-origin axes (<see cref="AddDefaultWorldAxes"/> creates it based on this).</summary>
    public bool ShowWorldAxes { get; set; } = true;

    /// <summary>Length of the default world-origin axes.</summary>
    public float WorldAxesLength { get; set; } = 0.5f;

    /// <summary>Cell edge length (meters).</summary>
    public float GridCellSize { get; set; } = 1f;

    /// <summary>Cell count from the center in each direction (total span = 2 × GridCellCount × GridCellSize).</summary>
    public int GridCellCount { get; set; } = 10;

    /// <summary>Grid line color.</summary>
    public Vector4 GridColor { get; set; } = new(0.30f, 0.30f, 0.35f, 1f);

    /// <summary>All lights in the scene (collected automatically by <see cref="Add"/>).</summary>
    public IReadOnlyList<Light> Lights => _lights;

    private bool _disposed;

    /// <summary>
    /// Construction completes the default scene assembly: default camera pose, default lights, grid
    /// floor, world-origin axes. The host does not need to call these manually (it only adds its own
    /// content, such as a URDF robot or demo objects).
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

    /// <summary>Creates and adds the default global axes at the world origin based on <see cref="ShowWorldAxes"/>; does not create when off.</summary>
    public Axes? AddDefaultWorldAxes()
    {
        if (!ShowWorldAxes)
            return null;

        var axes = new Axes(WorldAxesLength, name: "world-axes") { Transform = { Position = Vector3.Zero } };
        axes.SetSubtreePickable(false);   // The axes are a display aid and should not participate in picking.
        Add(axes);
        return axes;
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
        _disposed = true;
    }
}
