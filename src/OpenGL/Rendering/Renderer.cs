using System;
using System.Collections.Generic;
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

    private readonly GL _gl;
    private readonly ShaderProgram _modelShader;
    /// <summary>Maximum point size supported by the driver (GL_POINT_SIZE_RANGE upper bound), used to clamp uPointSize when drawing point clouds.</summary>
    private readonly float _maxPointSize;

    private readonly Dictionary<RenderPassKind, ShaderProgram> _passShaders;
    private readonly Dictionary<MeshData, Mesh> _meshCache = new();
    private readonly Dictionary<LineData, LineMesh> _lineCache = new();
    private readonly Dictionary<PointCloud2Data, PointMesh> _pointCache = new();
    private readonly Dictionary<MaterialData, Material> _materialCache = new();
    private bool _disposed;

    /// <param name="device">The rendering context (device-layer entry), assembled by the Host.</param>
    public Renderer(GraphicsContext device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _gl = device.NativeGl;

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
    }

    /// <summary>Gets the shader program for a pass (for future line/point/skybox drawing extensions).</summary>
    internal ShaderProgram GetPassShader(RenderPassKind pass) => _passShaders[pass];

    /// <summary>Draws the entire scene.</summary>
    public void Render(SceneGraph scene)
    {
        if (scene is null)
            throw new ArgumentNullException(nameof(scene));
        if (_disposed)
            throw new ObjectDisposedException(nameof(Renderer));

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
    /// Axes pass: an axis follows the object's placement/rotation but has a constant visual size (the
    /// shader does isotropic scaling). The constant-size parameters are managed by <see cref="Axes"/>
    /// itself (ScreenScale/Min/Max); the Renderer only reads them.
    /// </summary>
    private void DrawAxesNode(GameObject node, SceneGraph scene, Matrix4x4 view, Matrix4x4 projection)
    {
        Matrix4x4 model = node.Transform.GetModelMatrix();
        Vector3 origin = model.Translation;

        // Take only the pure rotation part of the model matrix (dropping parent scale) to get a
        // "scaleless world placement" matrix.
        Matrix4x4.Decompose(model, out _, out Quaternion rot, out _);
        Matrix4x4 axisModel = Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(origin);

        // The arrow is along local +Z with total length Arrow.Length, used as the reference length.
        float refLocalLen = (node as Arrow)?.Length ?? 1f;

        // Constant-size parameters come from the owning Axes (managed internally by the axes).
        Axes? axes = node.Transform.Parent?.Owner as Axes;
        float ratio = axes?.ScreenScale ?? Axes.DefaultScreenScale;
        float minLen = axes?.MinWorldLength ?? Axes.DefaultMinLength;
        float maxLen = axes?.MaxWorldLength ?? Axes.DefaultMaxLength;

        ShaderProgram shader = GetPassShader(RenderPassKind.Axes);
        shader.Use();
        shader.SetUniform("uAxesModel", axisModel);
        shader.SetUniform("uAxesOrigin", origin);
        shader.SetUniform("uAxesRefLocalLen", refLocalLen);
        shader.SetUniform("uAxesRatio", ratio);
        shader.SetUniform("uAxesMinLength", minLen);
        shader.SetUniform("uAxesMaxLength", maxLen);
        shader.SetUniform("uViewPos", scene.Camera.Position);
        shader.SetUniform("uView", view);
        shader.SetUniform("uProjection", projection);
        shader.SetUniform("uColor", node.MaterialData!.BaseColor);

        GetOrCreateMesh(node.MeshData!).Draw();
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

        GetOrCreate(_pointCache, data, () => new PointMesh(_gl, data)).Draw();
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
    private static T GetOrCreate<TKey, T>(Dictionary<TKey, T> cache, TKey key, Func<T> factory)
        where TKey : notnull
    {
        if (!cache.TryGetValue(key, out T? value))
        {
            value = factory();
            cache[key] = value;
        }

        return value;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        DisposeAll(_meshCache.Values);
        DisposeAll(_lineCache.Values);
        DisposeAll(_pointCache.Values);
        DisposeAll(_materialCache.Values);
        foreach (ShaderProgram shader in _passShaders.Values)
            shader.Dispose();
        _disposed = true;
    }

    private static void DisposeAll<T>(IEnumerable<T> resources)
        where T : IDisposable
    {
        foreach (T resource in resources)
            resource.Dispose();
    }
}
