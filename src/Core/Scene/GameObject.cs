using System;
using System.Collections.Generic;
using System.Numerics;
using RobotSimulation.Core.Geometry;
using RobotSimulation.Core.Geometry.Import;
using RobotSimulation.Core.Rendering;

namespace RobotSimulation.Core.Scene;

/// <summary>
/// Scene node. Holds only scene data: Transform, model data (CPU), and material description (CPU).
/// It manages no GPU resources — the GPU instantiation of meshes/materials is done by the renderer at
/// render time. So it can be created on any thread, reused across scenes, and after being added to a
/// SceneGraph it is traversed and drawn by the renderer.
/// </summary>
public class GameObject
{
    /// <summary>
    /// Object name, used for debugging and by-name lookup (e.g. URDF link/joint names).
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Local transform: position/rotation/scale relative to <see cref="Core.Scene.Transform.Parent"/>,
    /// plus the child list that makes the scene a tree.
    /// </summary>
    public Transform Transform { get; }

    /// <summary>3D model data (CPU vertices/indices). null means the node has no geometry (a skeleton/pure hierarchy node).</summary>
    public MeshData? MeshData { get; set; }

    /// <summary>
    /// Line data (CPU line set, used with <see cref="MaterialData.PassKind"/> = Line).
    /// Used for unlit lines such as grid floors and axes; mutually exclusive with <see cref="MeshData"/>.
    /// </summary>
    public LineData? LineData { get; set; }

    /// <summary>
    /// Point data (CPU, used with <see cref="MaterialData.PassKind"/> = Point, e.g. point clouds).
    /// </summary>
    public PointCloud2Data? PointData { get; set; }

    /// <summary>Rendering size (in pixels) for GL_POINTS; used by the Point pass.</summary>
    public float PointSize { get; set; } = 3f;

    /// <summary>
    /// This node's local coordinate axes (child object): when enabled, a small RGB axes set is attached
    /// under its Transform and moves/rotates/scales with it, rendered by ordinary recursion with no
    /// renderer special-casing.
    /// </summary>
    public bool ShowLocalAxes
    {
        get => _showLocalAxes;
        set
        {
            if (_showLocalAxes == value)
                return;
            _showLocalAxes = value;
            if (value)
            {
                _localAxes ??= new Axes(LocalAxesLength, name: "local-axes");
                _localAxes.SetSubtreePickable(
                    false); // The local axes are a display aid and should not participate in picking.
                _localAxes.Transform.Parent = Transform;
            }
            else
            {
                if (_localAxes is not null)
                    _localAxes.Transform.Parent =
                        null; // Detach from the tree (keep the object so it can be reattached).
            }
        }
    }

    /// <summary>Default length of the local axes (created at mount time; re-mount to change after).</summary>
    public float LocalAxesLength { get; set; } = 0.3f;

    /// <summary>The mounted local axes (non-null once <see cref="ShowLocalAxes"/> is enabled; can be tuned).</summary>
    public Axes? LocalAxes => _localAxes;

    private bool _showLocalAxes;
    private Axes? _localAxes;

    // Lazily created: nodes with no update logic (the common case) never allocate a list.
    private List<IUpdateBehavior>? _updateBehaviors;

    /// <summary>Material description (CPU). null means the node is not drawn (or uses a default appearance).</summary>
    public MaterialData? MaterialData { get; set; }

    /// <summary>Whether the renderer draws this node and its subtrees (invisible nodes are skipped unless a pick asks for them).</summary>
    public bool Visible { get; set; } = true;

    /// <summary>
    /// Whether highlighted (default false). The renderer does one thing for a highlighted node: blends
    /// the final color toward <see cref="HighlightColor"/> (tint), for selection visual feedback.
    /// Default off; the robot (<c>RobotModel</c>) is not highlighted by default, and the host sets it to
    /// true on a mouse hit via <see cref="SceneGraph.PickAndHighlight"/>.
    /// </summary>
    public bool Highlighted { get; set; }

    /// <summary>Highlight color (read by the rendering backend when <see cref="Highlighted"/> is true; CPU data only).</summary>
    public Vector4 HighlightColor { get; set; } = new(1f, 0.72f, 0.16f, 1f);

    /// <summary>
    /// Whether to participate in ray picking (<see cref="SceneGraph.Pick"/>). Default true.
    /// Display aids auto-assembled in the scene (world/local axes, grid floor, etc.) set this to false
    /// so they do not block the picking of target objects; the user can also disable an entire subtree.
    /// </summary>
    public bool Pickable { get; set; } = true;

    /// <summary>
    /// Recursively sets <see cref="Pickable"/> for this node and all descendants (for blanking out a
    /// subtree, such as axes).
    /// </summary>
    public void SetSubtreePickable(bool value)
    {
        Pickable = value;
        foreach (var child in Transform.Children)
            child.Owner.SetSubtreePickable(value);
    }

    /// <summary>Creates a node; the Transform is always allocated, the optionally supplied data fields are just assigned.</summary>
    /// <param name="meshData">Triangle geometry to draw (Model pass); null for a pure hierarchy node.</param>
    /// <param name="materialData">Appearance/pass description; null means the node is not drawn.</param>
    /// <param name="name">Scene name (used for debugging and by-name lookup); empties fall back to <see cref="string.Empty"/>.</param>
    public GameObject(MeshData? meshData = null, MaterialData? materialData = null, string? name = "")
    {
        Transform = new Transform(this);
        MeshData = meshData;
        MaterialData = materialData;
        if (!string.IsNullOrEmpty(name))
            Name = name;
    }

    /// <summary>
    /// Loads geometry and material from an external model file (STL/OBJ/DAE/glTF, via
    /// <see cref="AssimpModelLoader"/>), filling this object's <see cref="MeshData"/> /
    /// <see cref="MaterialData"/> (pure CPU data, callable from any thread).
    /// Only for models that "contain exactly one mesh"; for multi-submesh files use
    /// <see cref="AssimpModelLoader.Load"/> to get the full result and group it yourself.
    /// </summary>
    /// <param name="filePath">Model file path.</param>
    /// <param name="options">Import options (flip, scale, etc.); null uses defaults.</param>
    public void LoadModel(string filePath, LoadOptions? options = null)
    {
        LoadedModel model = AssimpModelLoader.Load(filePath, options);
        if (model.Meshes.Count != 1)
            throw new InvalidOperationException(
                $"{model.FilePath} contains {model.Meshes.Count} submeshes: GameObject.LoadModel only supports single-mesh models, use AssimpModelLoader.Load instead.");

        LoadedMesh mesh = model.Meshes[0];
        MeshData = mesh.MeshData;
        MaterialData = mesh.MaterialData;
    }

    // ------------------------------------------------------------------
    // Update behaviors (composition over inheritance)
    //
    // Per-frame logic is injected from outside instead of being written into a subclass: attach any number
    // of IUpdateBehavior instances to a node and SceneGraph.Update runs them once per frame, in attach
    // order and parent before child. They run on the update thread and may only mutate CPU data (never GL).
    // ------------------------------------------------------------------

    /// <summary>
    /// Attached update behaviors, in execution order (empty when none). Attach with
    /// <see cref="AddBehavior{T}"/> / <see cref="AddUpdate(Action{GameObject, double})"/>, detach with
    /// <see cref="RemoveBehavior"/> / <see cref="ClearBehaviors"/>.
    /// </summary>
    public IReadOnlyList<IUpdateBehavior> UpdateBehaviors
        => _updateBehaviors ?? (IReadOnlyList<IUpdateBehavior>)Array.Empty<IUpdateBehavior>();

    /// <summary>Number of attached update behaviors (0 means the node does no per-frame work).</summary>
    public int UpdateBehaviorCount => _updateBehaviors?.Count ?? 0;

    /// <summary>
    /// Attaches an update behavior: it runs every frame until removed. The generic return lets the caller
    /// keep the instance for later removal or <see cref="IUpdateBehavior.Enabled"/> control.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="behavior"/> is null.</exception>
    public T AddBehavior<T>(T behavior) where T : IUpdateBehavior
    {
        if (behavior is null)
            throw new ArgumentNullException(nameof(behavior));

        (_updateBehaviors ??= new List<IUpdateBehavior>()).Add(behavior);
        return behavior;
    }

    /// <summary>
    /// Injects a lambda update receiving this node and the frame's delta time (seconds). Returns the wrapper
    /// so the caller can disable or remove it later.
    /// </summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="update"/> is null.</exception>
    public DelegateUpdateBehavior AddUpdate(Action<GameObject, double> update)
        => AddBehavior(new DelegateUpdateBehavior(update));

    /// <summary>Convenience overload for updates that do not need the owning node.</summary>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="update"/> is null.</exception>
    public DelegateUpdateBehavior AddUpdate(Action<double> update)
    {
        if (update is null)
            throw new ArgumentNullException(nameof(update));
        return AddUpdate((_, deltaTime) => update(deltaTime));
    }

    /// <summary>Detaches a behavior; returns true when it was attached (the instance can be re-added later).</summary>
    public bool RemoveBehavior(IUpdateBehavior behavior)
    {
        if (behavior is null)
            return false;
        return _updateBehaviors?.Remove(behavior) ?? false;
    }

    /// <summary>Removes every attached update behavior.</summary>
    public void ClearBehaviors()
    {
        _updateBehaviors?.Clear();
    }

    /// <summary>
    /// Per-frame dispatch (called by <see cref="SceneGraph.Update"/> on the update thread): runs the attached
    /// behaviors in attach order, skipping disabled ones. Behaviors may only mutate CPU data, never GL.
    /// Mutating this node's own behavior list from inside its own update is not supported — use
    /// <see cref="IUpdateBehavior.Enabled"/> to pause instead.
    /// </summary>
    public void Update(double deltaTime)
    {
        List<IUpdateBehavior>? behaviors = _updateBehaviors;
        if (behaviors is null)
            return;

        for (int i = 0; i < behaviors.Count; i++)
        {
            IUpdateBehavior behavior = behaviors[i];
            if (behavior.Enabled)
                behavior.Update(this, deltaTime);
        }
    }
}