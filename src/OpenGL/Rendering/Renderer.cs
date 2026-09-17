using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using RobotSimulation.Core.Geometry;
using RobotSimulation.Core.Rendering;
using RobotSimulation.Core.Scene;
using RobotSimulation.OpenGL.Device;
using RobotSimulation.OpenGL.Resources;
using Silk.NET.OpenGL;

namespace RobotSimulation.OpenGL.Rendering;

/// <summary>
/// Renderer (draw orchestration layer): traverses the scene and, for each visible node carrying
/// <see cref="GameObject.MeshData"/> and <see cref="GameObject.MaterialData"/>, performs the
/// "data → GPU resource" instantiation and drawing. Implements <see cref="IRenderer"/>, depending only
/// on <see cref="GraphicsContext"/> (the device layer) and never receiving GL directly.
/// </summary>
public sealed class Renderer : IRenderer
{
    /// <summary>Maximum number of lights for a single pass (matches MAX_LIGHTS in Standard.frag).</summary>
    public const int MaxLights = 8;

    /// <summary>Highlight tint blend factor: how much a highlighted node's final color blends toward HighlightColor (0~1).</summary>
    public const float HighlightBlend = 0.30f;

    /// <summary>Exponential moving-average factor for frame-time smoothing (0~1; smaller = smoother).</summary>
    private const double FrameSmoothing = 0.1;

    /// <summary>Frames between two sweeps of the GPU resource caches.</summary>
    private const int SweepIntervalFrames = 120;

    /// <summary>Frames a cached resource may go unused before its buffers are released.</summary>
    private const int MaxIdleFrames = 240;

    /// <summary>Distance of the orientation gizmo's eye from its origin (world units; only the direction matters).</summary>
    private const float GizmoEyeDistance = 3f;

    /// <summary>
    /// Edge length of the orientation gizmo's orthographic view, in world units. The arrows are 1 unit long, so a
    /// width of 2.2 makes the marker fill its square (≈72 px per axis at the 160 px default) with just enough room
    /// left for the arrow heads — the widget draws nothing else around them, so the square viewport is all the
    /// margin it needs.
    /// </summary>
    private const float GizmoViewWidth = 2.2f;

    /// <summary>Radius of the hub ball at the gizmo's origin, in the gizmo's own units (≈7 px at the default size).</summary>
    private const float GizmoHubRadius = 0.10f;

    private readonly GL _gl;
    private readonly GraphicsContext _device;
    private readonly ShaderProgram _modelShader;

    /// <summary>
    /// CPU-side axes set the orientation gizmo draws: the same three arrows a world axes set uses, drawn through
    /// the same axes pass. <see cref="AxesSizing.FixedWorldLength"/> with a 1-unit length keeps the marker at a
    /// constant pixel size under the gizmo's orthographic projection, whatever the camera distance is.
    /// </summary>
    private readonly Axes _gizmoAxes = new(1f, shaftRadius: 0.032f, headRadius: 0.095f, headLength: 0.28f,
        name: "orientation-gizmo")
    {
        Sizing = AxesSizing.FixedWorldLength,
    };

    /// <summary>Maximum point size supported by the driver (GL_POINT_SIZE_RANGE upper bound), used to clamp uPointSize when drawing point clouds.</summary>
    private readonly float _maxPointSize;

    private readonly Dictionary<RenderPassKind, ShaderProgram> _passShaders;
    private readonly Dictionary<MeshData, Cached<Mesh>> _meshCache = new();
    private readonly Dictionary<LineData, Cached<LineMesh>> _lineCache = new();
    private readonly Dictionary<PointCloud2Data, Cached<PointMesh>> _pointCache = new();
    private readonly Dictionary<MaterialData, Cached<Material>> _materialCache = new();
    private long _lastSweepFrame;
    private bool _disposed;

    private readonly Stopwatch _frameClock = new();
    private double _smoothedFrameMs;
    private long _frameCount;
    private FrameStats _stats = FrameStats.Empty;

    /// <param name="device">The rendering context (device-layer entry), assembled by the Host.</param>
    public Renderer(GraphicsContext device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _gl = device.NativeGl;
        _device = device;

        // Compile all embedded standard pass shaders (Model/Line/Point/Skybox/Axes) — fail fast at startup
        // rather than discovering a GLSL error mid-run; the host does not manage shader file paths.
        _passShaders = new Dictionary<RenderPassKind, ShaderProgram>();
        foreach (RenderPassKind pass in Enum.GetValues<RenderPassKind>())
        {
            (string vs, string fs) = EmbeddedShaders.Get(pass);
            _passShaders[pass] = new ShaderProgram(_gl, vs, fs);
        }

        // The point-cloud vertex shader outputs point size via gl_PointSize. This is only effective when
        // GL_PROGRAM_POINT_SIZE is enabled, otherwise it falls back to the default 1px from glPointSize().
        // It only affects GL_POINTS and is used only by point clouds, so enable it once here; also query
        // the driver's maximum point size so uPointSize is clamped within bounds when drawing.
        _gl.Enable(EnableCap.ProgramPointSize);
        _gl.GetFloat(GLEnum.PointSizeRange, out Vector2 pointSizeRange);
        _maxPointSize = pointSizeRange.Y;

        _modelShader = _passShaders[RenderPassKind.Model];

        // The gizmo's hub: a small unlit ball at the marker's origin that hides the three shafts' butts and gives
        // the widget a solid pivot. It is an extra child of the same 1-unit axes set, so the axes pass leaves it at
        // scale 1 (only arrow geometry is stretched to the set's length) and the arrows start where it ends.
        var hub = new Sphere(GizmoHubRadius, segments: 24, rings: 12, name: "orientation-gizmo-hub")
        {
            Transform = { Parent = _gizmoAxes.Transform },
        };
        hub.MaterialData!.PassKind = RenderPassKind.Axes;
        hub.MaterialData.BaseColor = new Vector4(0.87f, 0.89f, 0.93f, 1f);
    }

    /// <summary>Gets the shader program for a pass (for future line/point/skybox drawing extensions).</summary>
    internal ShaderProgram GetPassShader(RenderPassKind pass) => _passShaders[pass];

    /// <inheritdoc />
    public FrameStats Stats => _stats;

    /// <summary>
    /// Updates frame timing from the interval between consecutive <see cref="Render"/> calls (render
    /// thread). An exponential moving average smooths per-frame jitter.
    /// </summary>
    private void UpdateStats()
    {
        if (!_frameClock.IsRunning)
        {
            // First frame: there is no interval to measure yet — just start the clock.
            _frameClock.Restart();
            return;
        }

        double frameMs = _frameClock.Elapsed.TotalMilliseconds;
        _frameClock.Restart();

        _smoothedFrameMs = _smoothedFrameMs <= 0
            ? frameMs
            : _smoothedFrameMs + (frameMs - _smoothedFrameMs) * FrameSmoothing;

        _frameCount++;
        _stats = new FrameStats(
            _smoothedFrameMs > 0 ? 1000.0 / _smoothedFrameMs : 0,
            frameMs,
            _smoothedFrameMs,
            _frameCount);
    }

    /// <summary>Draws the entire scene.</summary>
    public void Render(SceneGraph scene)
    {
        if (scene is null)
            throw new ArgumentNullException(nameof(scene));
        if (_disposed)
            throw new ObjectDisposedException(nameof(Renderer));

        UpdateStats();
        SweepUnusedCaches();

        Matrix4x4 view = scene.Camera.GetViewMatrix();
        Matrix4x4 projection = scene.Camera.GetProjectionMatrix();

        // Collect light parameters: each light corresponds to one slot in the uniform array.
        var colors = new Vector3[MaxLights];
        var positions = new Vector3[MaxLights];
        var directions = new Vector3[MaxLights];
        var types = new int[MaxLights];
        var intensities = new float[MaxLights];
        int lightCount = 0;

        foreach (Light light in scene.Lights)
        {
            if (lightCount >= MaxLights) break;
            colors[lightCount] = light.Color;
            positions[lightCount] = light.Position;
            directions[lightCount] = light.Direction;
            types[lightCount] = light.Type == LightType.Directional ? 1 : 0;
            intensities[lightCount] = light.Intensity;
            lightCount++;
        }

        ApplyLightUniforms(colors, positions, directions, types, intensities, lightCount);

        foreach (GameObject root in scene.Roots)
            RenderNode(root, scene, view, projection);

        // Last, so it lands on top of the finished picture rather than inside it (see the method for how its
        // own viewport and depth clear are confined to the corner square).
        if (scene.ShowOrientationGizmo)
            DrawOrientationGizmo(scene);
    }

    /// <summary>Writes the light parameters into the default shader's uniform array.</summary>
    private void ApplyLightUniforms(Vector3[] colors, Vector3[] positions, Vector3[] directions,
        int[] types, float[] intensities, int count)
    {
        // The model shader must be bound before writing uniforms: glUniform* applies to the currently
        // bound program. If the last drawn object in the previous frame was a point cloud/line/axes pass,
        // the bound program is not Model; without this line the lights would be written into the wrong
        // program (model uLightCount stays 0 → flat gray) and could pollute point/line shader uniforms.
        // Explicitly binding here fixes cross-pass residue.
        _modelShader.Use();

        _modelShader.SetUniform("uLightCount", count);
        _modelShader.SetUniform("uLightColors", colors);
        _modelShader.SetUniform("uLightPositions", positions);
        _modelShader.SetUniform("uLightDirections", directions);
        _modelShader.SetUniform("uLightTypes", types);
        _modelShader.SetUniform("uLightIntensities", intensities);
    }

    private void RenderNode(GameObject node, SceneGraph scene, Matrix4x4 view, Matrix4x4 projection)
    {
        // Visible nodes carrying data: dispatch by render pass.
        // Pure skeleton/hierarchy nodes (no data) only act as parents for recursion and do not block the
        // subtree (common for URDF links).
        if (node.Visible && node.MaterialData != null)
        {
            switch (node.MaterialData.PassKind)
            {
                case RenderPassKind.Model when node.MeshData != null:
                    DrawModelNode(node, scene, view, projection);
                    break;

                case RenderPassKind.Line when node.LineData != null:
                    DrawLineNode(node, view, projection);
                    break;

                case RenderPassKind.Point when node.PointData != null:
                    DrawPointNode(node, view, projection);
                    break;

                case RenderPassKind.Axes when node.MeshData != null:
                    DrawAxesNode(node, scene, view, projection);
                    break;

                // Skybox is a scene-level environment pass, not drawn by an ordinary node.
                case RenderPassKind.Skybox:
                default:
                    break;
            }
        }

        foreach (Transform child in node.Transform.Children)
            RenderNode(child.Owner, scene, view, projection);
    }

    /// <summary>
    /// Axes pass: an axis follows the object's placement/rotation, while its visible length is decided by the
    /// owning <see cref="Axes"/> — constant on screen, or fixed in world units. The Renderer only reads those
    /// parameters; see <see cref="DrawAxesPassNode"/>.
    /// </summary>
    private void DrawAxesNode(GameObject node, SceneGraph scene, Matrix4x4 view, Matrix4x4 projection)
        => DrawAxesPassNode(node, scene.Camera.Position, view, projection);

    /// <summary>
    /// Draws one node of an axes set through the axes pass: only the node's world rotation and origin are used, and
    /// the shader stretches its geometry to the length the owning <see cref="Axes"/> asks for. Used for the arrows
    /// and for the gizmo's hub ball — the pass leaves that at scale 1, since only <see cref="Arrow"/> geometry is
    /// stretched. Shared by the scene's axes sets and by the screen-space orientation gizmo, which is why the eye
    /// position is a parameter instead of being read from the camera.
    /// </summary>
    private void DrawAxesPassNode(GameObject node, Vector3 eyePosition, Matrix4x4 view, Matrix4x4 projection)
    {
        Matrix4x4 model = node.Transform.GetModelMatrix();
        Vector3 origin = model.Translation;

        // Take only the pure rotation part of the model matrix (dropping parent scale) to get a
        // "scaleless world placement" matrix.
        Matrix4x4.Decompose(model, out _, out Quaternion rot, out _);
        Matrix4x4 axisModel = Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(origin);

        // Arrows run along local +Z with total length Arrow.Length, used as the reference length; anything else in
        // an axes set (the gizmo's hub) is a unit-sized shape the shader must leave alone.
        float refLocalLen = (node as Arrow)?.Length ?? 1f;

        // Length policy, managed internally by the owning Axes and only read here. FixedWorldLength needs no
        // shader change: the shader computes clamp(ratio * cameraDistance, min, max), so ratio = 0 with both
        // bounds equal to the wanted length is exactly that length in world units.
        float ratio = Axes.DefaultScreenScale;
        float minLen = Axes.DefaultMinLength;
        float maxLen = Axes.DefaultMaxLength;
        if (node.Transform.Parent?.Owner is Axes axes)
        {
            if (axes.Sizing == AxesSizing.FixedWorldLength)
            {
                ratio = 0f;
                minLen = maxLen = axes.Length;
            }
            else
            {
                ratio = axes.ScreenScale;
                minLen = axes.MinWorldLength;
                maxLen = axes.MaxWorldLength;
            }
        }

        ShaderProgram shader = GetPassShader(RenderPassKind.Axes);
        shader.Use();
        shader.SetUniform("uAxesModel", axisModel);
        shader.SetUniform("uAxesOrigin", origin);
        shader.SetUniform("uAxesRefLocalLen", refLocalLen);
        shader.SetUniform("uAxesRatio", ratio);
        shader.SetUniform("uAxesMinLength", minLen);
        shader.SetUniform("uAxesMaxLength", maxLen);
        shader.SetUniform("uViewPos", eyePosition);
        shader.SetUniform("uView", view);
        shader.SetUniform("uProjection", projection);
        shader.SetUniform("uColor", node.MaterialData!.BaseColor);

        GetOrCreateMesh(node.MeshData!).Draw();
    }

    /// <summary>
    /// Draws the orientation gizmo: a screen-space axes widget — three RGB arrows and a hub ball, nothing else —
    /// pinned to the viewport's bottom-right corner and following the camera's orientation (see
    /// <see cref="SceneGraph.ShowOrientationGizmo"/>). It is the screen-space counterpart of a scene axes set: it is
    /// drawn after the scene, into its own viewport, with the depth buffer cleared inside that square, so it is
    /// never occluded by geometry, keeps a constant pixel size at any zoom, and, being no scene node, can never be
    /// picked.
    /// </summary>
    private void DrawOrientationGizmo(SceneGraph scene)
    {
        (int surfaceWidth, int surfaceHeight) = _device.ViewportSize;
        if (surfaceWidth <= 0 || surfaceHeight <= 0)
            return;   // The host has not sized a viewport yet: there is nothing to anchor the gizmo to.

        int size = (int)MathF.Round(Math.Clamp(scene.OrientationGizmoSize, 16f,
            MathF.Max(16f, Math.Min(surfaceWidth, surfaceHeight))));
        int margin = (int)MathF.Round(MathF.Max(0f, scene.OrientationGizmoMargin));
        int left = Math.Max(0, surfaceWidth - size - margin);
        int bottom = Math.Max(0, margin);   // GL's origin is the bottom-left corner, so this is the bottom-right one.

        // The scissor box is what confines the depth clear to the gizmo's square: glClear ignores the viewport, so
        // without it the whole frame's depth would be wiped (harmless here, the scene is already drawn, but not
        // something to rely on).
        _gl.Enable(EnableCap.ScissorTest);
        _gl.Scissor(left, bottom, (uint)size, (uint)size);
        _gl.Viewport(left, bottom, (uint)size, (uint)size);
        _gl.Clear(ClearBufferMask.DepthBufferBit);

        // The marker is always drawn solid: ApplyRenderState is per material, so the last model node may have
        // left a wireframe polygon mode behind and a wireframe gizmo is not a legible marker.
        _gl.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);

        // Orientation only: the eye keeps the camera's direction (so the marker turns exactly with the view) but
        // sits at a fixed distance, and the orthographic projection turns that into a constant pixel size. Up is
        // +Z — the same convention Camera.GetViewMatrix uses.
        Camera camera = scene.Camera;
        Vector3 fromTarget = camera.Position - camera.Target;
        Vector3 eye = fromTarget.LengthSquared() > 1e-8f
            ? Vector3.Normalize(fromTarget) * GizmoEyeDistance
            : new Vector3(0f, -GizmoEyeDistance, 0f);
        Matrix4x4 view = Matrix4x4.CreateLookAt(eye, Vector3.Zero, Vector3.UnitZ);
        Matrix4x4 projection = Matrix4x4.CreateOrthographic(GizmoViewWidth, GizmoViewWidth, 0.1f, 10f);

        // Straight to the marker: the widget is just the three arrows plus the hub ball, so there is no backdrop to
        // lay down first and nothing can hide it inside its own square.
        foreach (Transform child in _gizmoAxes.Transform.Children)
        {
            if (child.Owner.MeshData is null)
                continue;   // A child without geometry has nothing to draw; the arrows always carry a mesh.
            DrawAxesPassNode(child.Owner, eye, view, projection);
        }

        // Restore the full surface: the host owns the viewport and may set it only when its size changes (which is
        // what the bare-window host does), so leaving the gizmo's small viewport behind would shrink the whole
        // picture until the next resize.
        _gl.Disable(EnableCap.ScissorTest);
        _gl.Viewport(0, 0, (uint)surfaceWidth, (uint)surfaceHeight);
    }

    private void DrawModelNode(GameObject node, SceneGraph scene, Matrix4x4 view, Matrix4x4 projection)
    {
        Mesh mesh = GetOrCreateMesh(node.MeshData!);
        Material material = GetOrCreateMaterial(node.MaterialData!);

        // Render state bits: double-sided (cull off) / wireframe, set once before drawing.
        ApplyRenderState(node.MaterialData!);

        // Appearance parameters come directly from the CPU description (no mirrored copy, no manual sync).
        material.Apply(node.MaterialData!);
        ShaderProgram shader = material.Shader;
        shader.SetUniform("uModel", node.Transform.GetModelMatrix());
        shader.SetUniform("uView", view);
        shader.SetUniform("uProjection", projection);
        shader.SetUniform("uViewPos", scene.Camera.Position);
        shader.SetUniform("uAmbientColor", scene.AmbientColor);

        // Highlight tint: always write uHighlightMix (0 = no highlight) so a shared shader does not carry
        // the previous node's highlight over (when HighlightBlend > 0 the final color blends toward
        // HighlightColor, applied with or without textures).
        Vector4 hl = node.HighlightColor;
        shader.SetUniform("uHighlightMix", node.Highlighted ? HighlightBlend : 0f);
        shader.SetUniform("uHighlightColor", new Vector3(hl.X, hl.Y, hl.Z));

        mesh.Draw();
    }

    /// <summary>Line pass: unlit lines (grid floor / axes / curves, etc.).</summary>
    private void DrawLineNode(GameObject node, Matrix4x4 view, Matrix4x4 projection)
    {
        ShaderProgram shader = GetPassShader(RenderPassKind.Line);
        shader.Use();

        shader.SetUniform("uModel", node.Transform.GetModelMatrix());
        shader.SetUniform("uView", view);
        shader.SetUniform("uProjection", projection);
        shader.SetUniform("uColor", node.MaterialData!.BaseColor);
        shader.SetUniform("uPerVertexColor", node.LineData!.HasPerVertexColors ? 1 : 0);

        GetOrCreate(_lineCache, node.LineData!, () => new LineMesh(_gl, node.LineData!)).Draw();
    }

    /// <summary>Point pass: unlit point set (point cloud).</summary>
    private void DrawPointNode(GameObject node, Matrix4x4 view, Matrix4x4 projection)
    {
        PointCloud2Data data = node.PointData!;
        ShaderProgram shader = GetPassShader(RenderPassKind.Point);
        shader.Use();

        shader.SetUniform("uModel", node.Transform.GetModelMatrix());
        shader.SetUniform("uView", view);
        shader.SetUniform("uProjection", projection);
        shader.SetUniform("uColor", node.MaterialData!.BaseColor);
        shader.SetUniform("uPerVertexColor", data.HasColor ? 1 : 0);
        // GL_PROGRAM_POINT_SIZE is enabled at construction; clamp the size within [1, driver max].
        shader.SetUniform("uPointSize", MathF.Max(1f, MathF.Min(node.PointSize, _maxPointSize)));

        // The mesh owns the upload policy: it does nothing while the cloud's revision is unchanged,
        // and otherwise uploads only the changed slots (never the whole cloud on an ordinary update).
        PointMesh mesh = GetOrCreate(_pointCache, data, () => new PointMesh(_gl, data));
        mesh.Sync(data);
        mesh.Draw();
    }

    /// <summary>Toggles global GL state (culling / polygon mode) per the material's render state bits.</summary>
    private void ApplyRenderState(MaterialData material)
    {
        if (material.DoubleSided)
            _gl.Disable(EnableCap.CullFace);
        else
            _gl.Enable(EnableCap.CullFace);

        _gl.PolygonMode(TriangleFace.FrontAndBack,
            material.Wireframe ? PolygonMode.Line : PolygonMode.Fill);
    }

    private Mesh GetOrCreateMesh(MeshData data)
        => GetOrCreate(_meshCache, data, () => new Mesh(_gl, data.ToInterleavedArray(), data.ToIndexArray()));

    private Material GetOrCreateMaterial(MaterialData data)
    {
        return GetOrCreate(_materialCache, data, () => CreateMaterial(data));
    }

    /// <summary>Creates the GPU material on first encounter with a material description, uploading textures by CPU reference.</summary>
    private Material CreateMaterial(MaterialData data)
    {
        var material = new Material(_modelShader);
        if (data.AlbedoTexture != null)
            material.SetTexture(TextureType.Albedo, LoadTexture(data.AlbedoTexture));
        if (data.NormalTexture != null)
            material.SetTexture(TextureType.Normal, LoadTexture(data.NormalTexture));
        if (data.MetallicTexture != null)
            material.SetTexture(TextureType.Metallic, LoadTexture(data.MetallicTexture));
        if (data.RoughnessTexture != null)
            material.SetTexture(TextureType.Roughness, LoadTexture(data.RoughnessTexture));
        return material;
    }

    private Texture2D LoadTexture(TextureReference reference)
        => new(_gl, reference);

    /// <summary>Gets the cached value by reference key, creating and caching it via the factory on a miss.</summary>
    /// <remarks>Also stamps the entry with the current frame, which is what the sweeper expires entries by.</remarks>
    private T GetOrCreate<TKey, T>(Dictionary<TKey, Cached<T>> cache, TKey key, Func<T> factory)
        where TKey : notnull
    {
        if (!cache.TryGetValue(key, out Cached<T>? entry))
        {
            entry = new Cached<T>(factory(), _frameCount);
            cache[key] = entry;
        }

        entry.LastFrame = _frameCount;
        return entry.Value;
    }

    /// <summary>
    /// Releases cached GPU resources whose data has not been drawn for a while. Caches are keyed by the
    /// data reference, so replacing an object's data (or dropping the object from the scene) would
    /// otherwise keep the old GPU buffers alive until the renderer is disposed — a leak that grows with
    /// every replacement, exactly the pattern an incrementally updated scene is most likely to hit.
    /// </summary>
    private void SweepUnusedCaches()
    {
        if (_frameCount - _lastSweepFrame < SweepIntervalFrames)
            return;
        _lastSweepFrame = _frameCount;

        SweepUnused(_meshCache);
        SweepUnused(_lineCache);
        SweepUnused(_pointCache);
        SweepUnused(_materialCache);
    }

    private void SweepUnused<TKey, T>(Dictionary<TKey, Cached<T>> cache)
        where TKey : notnull
        where T : IDisposable
    {
        if (cache.Count == 0)
            return;

        List<TKey>? expired = null;
        foreach (KeyValuePair<TKey, Cached<T>> entry in cache)
        {
            if (_frameCount - entry.Value.LastFrame > MaxIdleFrames)
                (expired ??= new List<TKey>()).Add(entry.Key);
        }

        if (expired is null)
            return;

        foreach (TKey key in expired)
        {
            cache[key].Value.Dispose();
            cache.Remove(key);
        }
    }

    /// <summary>A cached GPU resource plus the last frame that used it.</summary>
    private sealed class Cached<T>
    {
        /// <param name="value">The GPU resource.</param>
        /// <param name="frame">Frame the resource was created on.</param>
        public Cached(T value, long frame)
        {
            Value = value;
            LastFrame = frame;
        }

        /// <summary>The GPU resource.</summary>
        public T Value { get; }

        /// <summary>Last frame that asked for this resource.</summary>
        public long LastFrame { get; set; }
    }

    /// <summary>
    /// Releases every cached GPU resource (meshes, lines, points, materials) and every pass shader.
    /// Must run while the GL context is still current; safe to call more than once.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        DisposeAll(_meshCache);
        DisposeAll(_lineCache);
        DisposeAll(_pointCache);
        DisposeAll(_materialCache);
        foreach (ShaderProgram shader in _passShaders.Values)
            shader.Dispose();
        _disposed = true;
    }

    private static void DisposeAll<TKey, T>(Dictionary<TKey, Cached<T>> cache)
        where TKey : notnull
        where T : IDisposable
    {
        foreach (Cached<T> entry in cache.Values)
            entry.Value.Dispose();
    }
}
