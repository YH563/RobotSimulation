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
                _localAxes.SetSubtreePickable(false);   // The local axes are a display aid and should not participate in picking.
                _localAxes.Transform.Parent = Transform;
            }
            else
            {
                if (_localAxes is not null)
                    _localAxes.Transform.Parent = null;   // Detach from the tree (keep the object so it can be reattached).
            }
        }
    }

    /// <summary>Default length of the local axes (created at mount time; re-mount to change after).</summary>
    public float LocalAxesLength { get; set; } = 0.3f;

    /// <summary>The mounted local axes (non-null once <see cref="ShowLocalAxes"/> is enabled; can be tuned).</summary>
    public Axes? LocalAxes => _localAxes;

    private bool _showLocalAxes;
    private Axes? _localAxes;

    /// <summary>Material description (CPU). null means the node is not drawn (or uses a default appearance).</summary>
    public MaterialData? MaterialData { get; set; }

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

    /// <summary>
    /// Per-frame update logic.
    /// </summary>
    /// <param name="deltaTime"></param>
    public virtual void Update(double deltaTime) { }
}
