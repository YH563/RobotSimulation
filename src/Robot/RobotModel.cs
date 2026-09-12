using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Xml;
using System.Xml.Linq;
using RobotSimulation.Core.Geometry.Import;
using RobotSimulation.Core.Rendering;
using RobotSimulation.Core.Scene;
using RobotSimulation.Core.Utils;
using RobotSimulation.Robot.Description;
using RobotSimulation.Robot.State;
using RobotSimulation.Robot.Urdf;

namespace RobotSimulation.Robot;

/// <summary>
/// A robot — a complete <see cref="GameObject"/> tree.
/// This type is the root of the tree (the root link's skeleton node is itself) and the single entry point
/// to URDF/descriptions: read a URDF or construct from any <see cref="RobotDescription"/> and immediately
/// get a drivable, extensible robot GameObject tree that can be added to a scene. Nodes hold only CPU data
/// (MeshData/MaterialData) with no rendering parameters — the caller adds it via scene.Add and the
/// renderer traverses and draws it.
///
/// Usage example:
/// <code>
/// var robot = RobotModel.ParseFile("arm.urdf");    // URDF → full robot tree.
/// scene.Add(robot);                                 // Added to the scene like any other GameObject.
/// robot.SetJointValue("shoulder", 1.2f);           // Drive a joint (radians for revolute, meters for prismatic).
///
/// // Extension point: any parser producing a RobotDescription (SDF/custom) can build the same robot tree.
/// var another = new RobotModel(someDescription);
/// </code>
/// </summary>
public sealed class RobotModel : GameObject
{
    private readonly RobotDescription _description;
    private readonly IAssetResolver? _resolver;

    // Used for every mesh/texture lookup. Never null: it is the caller's resolver when one was supplied,
    // otherwise the default filesystem resolver bound to the caller's asset directory.
    private readonly IAssetResolver _effectiveResolver;

    // Drivable joint bindings (in RobotDescription.Joints order).
    private readonly List<Joint> _drivableJoints = new();
    private readonly Dictionary<string, int> _drivableIndexByName = new(StringComparer.Ordinal);
    private readonly List<GameObject> _drivableChildLinks = new();
    private float[] _jointValues = Array.Empty<float>();

    // ------------------------------------------------------------------
    // Construction (extension point: any description source → a robot GameObject tree)
    // ------------------------------------------------------------------

    /// <summary>
    /// Builds the complete robot tree from a static description: each link is a skeleton GameObject
    /// (the root link = this object itself), visuals are child nodes carrying model data/material
    /// description, and joints attach each subtree under the parent link by joint.origin. The description
    /// must be single-root. GPU instantiation of meshes/materials is done by the renderer at draw time.
    /// </summary>
    /// <param name="description">The robot static description (URDF/SDF/custom parse result).</param>
    /// <param name="resolver">Asset path resolver (mesh/texture); null uses the default filesystem implementation.</param>
    /// <param name="assetDirectory">
    /// Extra directory searched for mesh/texture files before the URDF file's own directory (MuJoCo's
    /// <c>meshdir</c> idea); null keeps the default "next to the URDF" search. Only valid together with a null
    /// <paramref name="resolver"/> — see <see cref="RequireNoAssetDirectoryConflict"/>.
    /// </param>
    /// <exception cref="ArgumentException">A custom <paramref name="resolver"/> was combined with an <paramref name="assetDirectory"/>.</exception>
    public RobotModel(RobotDescription description, IAssetResolver? resolver = null, string? assetDirectory = null)
        : base(null, null, RequireSingleRoot(description))
    {
        RequireNoAssetDirectoryConflict(resolver, assetDirectory);
        _description = description;
        _resolver = resolver;
        _effectiveResolver = resolver ?? new FileSystemAssetResolver(assetDirectory);
        RobotName = description.Name ?? string.Empty;
        BuildTree();
        LockTree(); // Lock the whole subtree after construction: child-link poses can only change via RobotModel's built-in interface.
        // Not highlighted by default. Highlighting is pure click feedback, set by the host on a mouse hit
        // via SceneGraph.PickAndHighlight. To highlight the whole tree by default, call
        // SetSubtreeHighlight(this) manually (kept here for developers).
    }

    /// <summary>
    /// Guards the one contradictory combination: a caller-supplied resolver already owns asset lookup, so an
    /// <paramref name="assetDirectory"/> next to it could only be silently ignored. Fail loudly instead.
    /// </summary>
    private static void RequireNoAssetDirectoryConflict(IAssetResolver? resolver, string? assetDirectory)
    {
        if (resolver is not null && !string.IsNullOrWhiteSpace(assetDirectory))
            throw new ArgumentException(
                "assetDirectory cannot be combined with a custom IAssetResolver: the resolver decides where " +
                "assets are looked up. Pass either assetDirectory (uses FileSystemAssetResolver) or a resolver " +
                "configured with your own search root.",
                nameof(assetDirectory));
    }

    /// <summary>Validates the description is non-null and has exactly one root link, returning that root link name (this GameObject's Name).</summary>
    private static string RequireSingleRoot(RobotDescription? description)
    {
        if (description is null)
            throw new ArgumentNullException(nameof(description));
        if (description.RootLinks.Count != 1)
            throw new ArgumentException(
                $"Robot '{description.Name}' has {description.RootLinks.Count} root links; " +
                "building as a single GameObject requires exactly 1 root.",
                nameof(description));
        return description.RootLinks[0].Name;
    }

    // ------------------------------------------------------------------
    // URDF convenience factory: read URDF → full robot tree
    // ------------------------------------------------------------------

    /// <summary>Parses in-memory URDF text and builds the complete robot tree.</summary>
    /// <param name="urdfXml">URDF source text.</param>
    /// <param name="baseDirectory">Base directory for relative resource paths; may be null.</param>
    /// <param name="resolver">Asset path resolver (mesh/texture); may be null.</param>
    /// <param name="assetDirectory">
    /// Extra directory searched for mesh/texture files before <paramref name="baseDirectory"/> (MuJoCo's
    /// <c>meshdir</c> idea); null keeps the default "next to the URDF" search.
    /// </param>
    /// <exception cref="UrdfParseException">Thrown when the XML or URDF semantics are invalid.</exception>
    public static RobotModel Parse(string urdfXml, string? baseDirectory = null,
        IAssetResolver? resolver = null, string? assetDirectory = null)
    {
        if (urdfXml is null)
            throw new UrdfParseException("URDF text is null.");

        XDocument document;
        try
        {
            document = XDocument.Parse(urdfXml);
        }
        catch (XmlException ex)
        {
            throw new UrdfParseException($"URDF XML format error: {ex.Message}", ex);
        }

        RobotDescription description = new UrdfParser().Parse(document, baseDirectory);
        return new RobotModel(description, resolver, assetDirectory);
    }

    /// <summary>Reads a URDF file and builds the complete robot tree; the file's directory is automatically the base for relative resource paths.</summary>
    /// <param name="path">Path of the URDF file to read.</param>
    /// <param name="resolver">Asset path resolver (mesh/texture); may be null.</param>
    /// <param name="assetDirectory">
    /// Extra directory searched for mesh/texture files before the URDF file's own directory (MuJoCo's
    /// <c>meshdir</c> idea) — the way to load a URDF that keeps its meshes outside its own folder; null keeps
    /// the default "next to the URDF" search.
    /// </param>
    /// <exception cref="UrdfParseException">Thrown when parsing fails.</exception>
    /// <exception cref="FileNotFoundException">Thrown when the file does not exist.</exception>
    public static RobotModel ParseFile(string path, IAssetResolver? resolver = null, string? assetDirectory = null)
    {
        string fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"URDF file not found: {fullPath}", fullPath);

        string xml = File.ReadAllText(fullPath);
        return Parse(xml, Path.GetDirectoryName(fullPath), resolver, assetDirectory);
    }

    // ------------------------------------------------------------------
    // Description and headless (FK / serialization)
    // ------------------------------------------------------------------

    /// <summary>The pure-data description this tree was built from.</summary>
    public RobotDescription Description => _description;

    /// <summary>
    /// Robot name (URDF &lt;robot name&gt;). Distinct from <see cref="GameObject.Name"/> (the root link
    /// name) — GameObject.Name refers to the scene skeleton node's name.
    /// </summary>
    public string RobotName { get; }

    /// <summary>
    /// The caller-supplied asset path resolver, or null when the default <see cref="FileSystemAssetResolver"/>
    /// performs the lookups (itself bound to the <c>assetDirectory</c> given to the factory, if any).
    /// </summary>
    public IAssetResolver? AssetResolver => _resolver;

    /// <summary>Creates a headless pure-state object (no scene/render dependency): joint values + FK.</summary>
    public RobotState CreateState() => new RobotState(_description);

    /// <summary>
    /// The robot as a whole (root link) pose in its parent frame.
    /// Reads and writes go through this type's built-in interface: writes use
    /// <see cref="Transform.SetLocalPose"/> to bypass the read-only protection, and like
    /// <c>SetJointValue</c> it is the only entry that can change the whole tree's pose (the root link has
    /// no incoming joint, so the whole robot can be freely placed).
    /// </summary>
    public Matrix4x4 RootPose
    {
        get => Transform.GetLocalMatrix();
        set
        {
            if (Matrix4x4.Decompose(value, out Vector3 scale, out Quaternion rotation, out Vector3 position))
                Transform.SetLocalPose(position, rotation, scale);
            else
                Transform.SetLocalPose(value.Translation, Quaternion.Identity, Vector3.One);
        }
    }

    // ------------------------------------------------------------------
    // Tree building
    // ------------------------------------------------------------------

    private void BuildTree()
    {
        string rootLinkName = _description.RootLinks[0].Name;

        // 1) One skeleton node per link (root = this object itself).
        var linkNodes = new Dictionary<string, GameObject>(StringComparer.Ordinal) { [rootLinkName] = this };
        foreach (Link link in _description.Links)
        {
            if (link.Name == rootLinkName)
                continue;
            linkNodes[link.Name] = new GameObject(null, null, link.Name);
        }

        // 2) Mount the visual child nodes (carrying CPU model data and material description).
        foreach (Link link in _description.Links)
        {
            GameObject linkNode = linkNodes[link.Name];
            for (int i = 0; i < link.VisualElements.Count; i++)
            {
                foreach (GameObject visualNode in BuildVisualNodes(link.Name, i, link.VisualElements[i]))
                    visualNode.Transform.Parent = linkNode.Transform;
            }
        }

        // 3) Joint topology: mount each child link's skeleton under its parent, applying joint.origin (zero pose).
        foreach (Joint joint in _description.Joints)
        {
            GameObject parent = linkNodes[joint.ParentLinkName];
            GameObject child = linkNodes[joint.ChildLinkName];
            child.Transform.Parent = parent.Transform;
            ApplyPose(child.Transform, joint.Origin);
        }

        // 4) Register the drivable joint bindings.
        ConfigureDriving(linkNodes);
    }

    /// <summary>Collects the drivable joints (Revolute/Continuous/Prismatic, in description order) and their target child links.</summary>
    private void ConfigureDriving(IReadOnlyDictionary<string, GameObject> linkNodes)
    {
        foreach (Joint joint in _description.Joints)
        {
            if (joint.Type is not (JointType.Revolute or JointType.Continuous or JointType.Prismatic))
                continue;

            _drivableIndexByName[joint.Name] = _drivableJoints.Count;
            _drivableJoints.Add(joint);
            _drivableChildLinks.Add(linkNodes[joint.ChildLinkName]);
        }

        _jointValues = new float[_drivableJoints.Count];
    }

    // ------------------------------------------------------------------
    // Joint driving (visualization)
    // ------------------------------------------------------------------

    /// <summary>List of drivable joints (Revolute/Continuous/Prismatic, in description order).</summary>
    public IReadOnlyList<Joint> DrivableJoints => _drivableJoints;

    /// <summary>Number of drivable joints (i.e. the array length required by <see cref="ApplyJointValues"/>).</summary>
    public int DrivableJointCount => _drivableJoints.Count;

    /// <summary>
    /// Sets a joint value by name (radians for revolute, meters along the axis for prismatic) and
    /// immediately updates the corresponding child link's local Transform. Operates only on Transform
    /// (pure data); never touches OpenGL.
    /// </summary>
    public void SetJointValue(string name, float value)
    {
        int index = ResolveDrivable(name);
        _jointValues[index] = value;
        ApplyJointNode(index);
    }

    /// <summary>Sets joint values in bulk, in drivable-joint order, and updates the whole tree at once.</summary>
    public void ApplyJointValues(IReadOnlyList<float> values)
    {
        if (values is null)
            throw new ArgumentNullException(nameof(values));
        if (values.Count != _drivableJoints.Count)
            throw new ArgumentException(
                $"Joint value count ({values.Count}) does not match the drivable joint count ({_drivableJoints.Count}).",
                nameof(values));

        for (int i = 0; i < values.Count; i++)
        {
            _jointValues[i] = values[i];
            ApplyJointNode(i);
        }
    }

    private int ResolveDrivable(string name)
    {
        if (!_drivableIndexByName.TryGetValue(name, out int index))
            throw new KeyNotFoundException(
                $"Joint '{name}' does not exist or is not drivable (Fixed joints do not occupy a drive slot).");
        return index;
    }

    private void ApplyJointNode(int index)
    {
        Joint joint = _drivableJoints[index];
        GameObject childLink = _drivableChildLinks[index];

        // Child-link local pose = joint.origin * joint motion(q); row-major, consistent with GetModelMatrix chaining.
        Matrix4x4 local = joint.Origin * JointMotion(joint, _jointValues[index]);
        ApplyPose(childLink.Transform, local);
    }

    private static Matrix4x4 JointMotion(Joint joint, float q)
    {
        switch (joint.Type)
        {
            case JointType.Revolute:
            case JointType.Continuous:
                return Matrix4x4.CreateFromQuaternion(
                    Quaternion.CreateFromAxisAngle(joint.Axis, q));
            case JointType.Prismatic:
                return Matrix4x4.CreateTranslation(joint.Axis * q);
            default:
                return Matrix4x4.Identity;
        }
    }

    // ------------------------------------------------------------------
    // Visual / material building
    // ------------------------------------------------------------------

    /// <summary>
    /// Produces the drawable child nodes for one visual of a link.
    /// Primitive: a single primitive node; Mesh: parse the file and emit one node per submesh (each with
    /// its own material). Asset-resolution/import failures throw directly and log (no silent skip), so the
    /// user can fix the URDF path.
    /// </summary>
    private IReadOnlyList<GameObject> BuildVisualNodes(string linkName, int index, VisualElement visual)
    {
        if (visual.Geometry is MeshGeometry mesh)
            return LoadMeshVisual(linkName, index, visual, mesh);

        // Primitive geometry → the corresponding primitive GameObject (constructs the CPU MeshData immediately).
        GameObject node = BuildPrimitiveNode(visual.Geometry);
        node.Name = $"{linkName}:visual{index}";
        node.MaterialData = BuildMaterialData(visual.Material);
        ApplyPose(node.Transform, visual.LocalTransform);
        return new[] { node };
    }

    /// <summary>URDF mesh geometry → file import → child nodes (one per submesh; URDF material overrides the file material).</summary>
    private IReadOnlyList<GameObject> LoadMeshVisual(string linkName, int index, VisualElement visual,
        MeshGeometry mesh)
    {
        IAssetResolver resolver = _effectiveResolver;
        string path = resolver.Resolve(mesh.Uri, _description.SourceBaseDirectory)
                      ?? throw new FileNotFoundException(
                          $"Cannot resolve mesh asset '{mesh.Uri}' (link '{linkName}' visual#{index}). Check the URDF path.",
                          mesh.Uri);

        LoadedModel model;
        try
        {
            model = AssimpModelLoader.Load(path);
        }
        catch (Exception ex)
        {
            Logger.Error($"[RobotModel] mesh import failed: {path} (link '{linkName}' visual#{index}).", ex);
            throw new IOException(
                $"mesh import failed: {path} (link '{linkName}' visual#{index}). Check the file is intact and the format is supported.", ex);
        }

        string baseName = $"{linkName}:visual{index}";
        var nodes = new List<GameObject>(model.Meshes.Count);
        for (int m = 0; m < model.Meshes.Count; m++)
        {
            LoadedMesh sub = model.Meshes[m];
            GameObject node = new(
                sub.MeshData,
                MergeMeshMaterial(sub.MaterialData, visual.Material),
                model.Meshes.Count > 1 ? $"{baseName}.{m}" : baseName);
            ApplyPose(node.Transform, visual.LocalTransform);
            nodes.Add(node);
        }

        Logger.Debug($"[RobotModel] '{baseName}' ← {path} ({model.Meshes.Count} submeshes)");
        return nodes;
    }

    /// <summary>Merge rule: the file's own material is the baseline; an explicit URDF material's color/texture overrides it.</summary>
    private MaterialData MergeMeshMaterial(MaterialData fileMaterial, MaterialElement? urdf)
    {
        // Every import produces a fresh material instance, so it is safe to override in place (file material is the baseline).
        if (urdf?.Color is { } color)
            fileMaterial.BaseColor = color;
        if (urdf?.TextureFile is { } textureFile)
        {
            IAssetResolver resolver = _effectiveResolver;
            if (resolver.Resolve(textureFile, _description.SourceBaseDirectory) is { } texturePath)
                fileMaterial.AlbedoTexture = TextureReference.FromFile(texturePath);
        }

        return fileMaterial;
    }

    /// <summary>Instantiates a URDF primitive geometry into the corresponding primitive GameObject (Box/Sphere/Cylinder/Capsule).</summary>
    private static GameObject BuildPrimitiveNode(GeometryElement geometry)
    {
        return geometry switch
        {
            BoxGeometry box => new Box(box.Size.X, box.Size.Y, box.Size.Z),
            SphereGeometry sphere => new Sphere(sphere.Radius),
            CylinderGeometry cylinder => new Cylinder(cylinder.Radius, cylinder.Length),
            CapsuleGeometry capsule => new Capsule(capsule.Radius, capsule.Length),
            MeshGeometry => throw new InvalidOperationException("Mesh geometry should be handled at the outer layer."),
            _ => throw new ArgumentOutOfRangeException(nameof(geometry)),
        };
    }

    /// <summary>Converts a URDF material element into a CPU material description: color + albedo texture reference.</summary>
    private MaterialData BuildMaterialData(MaterialElement? material)
    {
        var data = new MaterialData
        {
            BaseColor = material?.Color ?? new Vector4(0.78f, 0.78f, 0.78f, 1f),
        };
        if (material?.TextureFile is { } textureFile)
        {
            IAssetResolver resolver = _effectiveResolver;
            if (resolver.Resolve(textureFile, _description.SourceBaseDirectory) is { } path)
                data.AlbedoTexture = TextureReference.FromFile(path);
        }

        return data;
    }

    /// <summary>Decomposes a row-major pose matrix (rotation + translation only) into Transform's Position/Rotation (keeping the existing Scale).</summary>
    private static void ApplyPose(Transform transform, Matrix4x4 pose)
    {
        if (Matrix4x4.Decompose(pose, out _, out Quaternion rotation, out Vector3 position))
            transform.SetLocalPose(position, rotation, transform.Scale);
        else
            transform.SetLocalPose(pose.Translation, Quaternion.Identity, transform.Scale);
    }

    /// <summary>Locks the whole subtree (including the root): any direct write to a child link's Transform afterward throws.</summary>
    private void LockTree() => LockTransform(Transform);

    private static void LockTransform(Transform transform)
    {
        transform.SetReadOnly(true);
        foreach (Transform child in transform.Children)
            LockTransform(child);
    }

    /// <summary>Sets <see cref="GameObject.Highlighted"/> to true for every mesh-carrying visual node in a subtree;
    /// highlight is a pure-data switch and the lock only constrains Transform poses, so a developer can turn it
    /// off/customize per node afterward.</summary>
    private static void SetSubtreeHighlight(GameObject node)
    {
        if (node.MeshData != null)
            node.Highlighted = true;
        foreach (Transform child in node.Transform.Children)
            SetSubtreeHighlight(child.Owner);
    }
}
