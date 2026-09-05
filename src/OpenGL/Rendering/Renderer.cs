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
/// 渲染器（绘制编排层）：遍历场景并对每个可见、且携带 <see cref="GameObject.MeshData"/> 与
/// <see cref="GameObject.MaterialData"/> 的节点执行"数据 → GPU 资源"的实例化与绘制。
/// 实现 <see cref="IRenderer"/>，只依赖 <see cref="GraphicsContext"/>（设备层），不直接接收 GL。
/// </summary>
public sealed class Renderer : IRenderer
{
    /// <summary>单 pass 支持的最大光源数（与 Standard.frag 的 MAX_LIGHTS 一致）。</summary>
    public const int MaxLights = 8;

    private readonly GL _gl;
    private readonly ShaderProgram _modelShader;

    private readonly Dictionary<RenderPassKind, ShaderProgram> _passShaders;
    private readonly Dictionary<MeshData, Mesh> _meshCache = new();
    private readonly Dictionary<LineData, LineMesh> _lineCache = new();
    private readonly Dictionary<PointCloudData, PointMesh> _pointCache = new();
    private readonly Dictionary<MaterialData, Material> _materialCache = new();
    private bool _disposed;

    /// <param name="device">渲染上下文（设备层入口），由 Host 组装。</param>
    public Renderer(GraphicsContext device)
    {
        ArgumentNullException.ThrowIfNull(device);
        _gl = device.NativeGl;

        // 编译全部内嵌标准通道着色器（Model/Line/Point/Skybox）——启动即失败提示，
        // 避免在运行途中才发现 GLSL 错误；宿主无需管理 shader 文件路径。
        _passShaders = new Dictionary<RenderPassKind, ShaderProgram>();
        foreach (RenderPassKind pass in Enum.GetValues<RenderPassKind>())
        {
            (string vs, string fs) = EmbeddedShaders.Get(pass);
            _passShaders[pass] = new ShaderProgram(_gl, vs, fs);
        }

        _modelShader = _passShaders[RenderPassKind.Model];
    }

    /// <summary>按通道取着色器程序（供后续线条/点云/天空等绘制扩展使用）。</summary>
    internal ShaderProgram GetPassShader(RenderPassKind pass) => _passShaders[pass];

    /// <summary>绘制整个场景。</summary>
    public void Render(SceneGraph scene)
    {
        if (scene is null)
            throw new ArgumentNullException(nameof(scene));
        if (_disposed)
            throw new ObjectDisposedException(nameof(Renderer));

        Matrix4x4 view = scene.Camera.GetViewMatrix();
        Matrix4x4 projection = scene.Camera.GetProjectionMatrix();

        // 收集光源参数：每个光源对应 uniform 数组的一个槽位
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

    /// <summary>把光源参数写入默认着色器的 uniform 数组。</summary>
    private void ApplyLightUniforms(Vector3[] colors, Vector3[] positions, Vector3[] directions,
        int[] types, float[] intensities, int count)
    {
        _modelShader.SetUniform("uLightCount", count);
        _modelShader.SetUniform("uLightColors", colors);
        _modelShader.SetUniform("uLightPositions", positions);
        _modelShader.SetUniform("uLightDirections", directions);
        _modelShader.SetUniform("uLightTypes", types);
        _modelShader.SetUniform("uLightIntensities", intensities);
    }

    private void RenderNode(GameObject node, SceneGraph scene, Matrix4x4 view, Matrix4x4 projection)
    {
        // 携带数据且可见的节点：按其渲染通道分派。
        // 纯骨架/层级节点（无数据）只作为父节点递归，不阻断子树（URDF link 常如此）。
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

                // Skybox 属场景级环境通道，不由普通节点绘制。
                case RenderPassKind.Skybox:
                default:
                    break;
            }
        }

        foreach (Transform child in node.Transform.Children)
            RenderNode(child.Owner, scene, view, projection);
    }

    /// <summary>
    /// 坐标轴通道：轴随对象摆放/旋转，但视觉尺寸恒定（着色器内做各向同性缩放）。
    /// 恒定尺寸参数由 <see cref="Axes"/> 自身管理（ScreenScale/Min/Max），Renderer 只读取。
    /// </summary>
    private void DrawAxesNode(GameObject node, SceneGraph scene, Matrix4x4 view, Matrix4x4 projection)
    {
        Matrix4x4 model = node.Transform.GetModelMatrix();
        Vector3 origin = model.Translation;

        // 取模型矩阵纯旋转部分（去掉父级缩放），得到"无缩放的世界摆放"矩阵
        Matrix4x4.Decompose(model, out _, out Quaternion rot, out _);
        Matrix4x4 axisModel = Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(origin);

        // 箭头沿局部 +Z、总长为 Arrow.Length，用作参考长度
        float refLocalLen = (node as Arrow)?.Length ?? 1f;

        // 恒定尺寸参数来自所属的 Axes（坐标轴内部管理）
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

        // 渲染状态位：双面（关闭背面剔除）/ 线框，绘制前一次性设置
        ApplyRenderState(node.MaterialData!);

        // 外观参数直接来自 CPU 描述（无镜像副本，无需手动同步）
        material.Apply(node.MaterialData!);
        ShaderProgram shader = material.Shader;
        shader.SetUniform("uModel", node.Transform.GetModelMatrix());
        shader.SetUniform("uView", view);
        shader.SetUniform("uProjection", projection);
        shader.SetUniform("uViewPos", scene.Camera.Position);
        shader.SetUniform("uAmbientColor", scene.AmbientColor);

        mesh.Draw();
    }

    /// <summary>Line 通道：无线光线条（网格地面/坐标轴/曲线等）。</summary>
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

    /// <summary>Point 通道：无光照点集（点云）。</summary>
    private void DrawPointNode(GameObject node, Matrix4x4 view, Matrix4x4 projection)
    {
        PointCloudData data = node.PointData!;
        ShaderProgram shader = GetPassShader(RenderPassKind.Point);
        shader.Use();

        shader.SetUniform("uModel", node.Transform.GetModelMatrix());
        shader.SetUniform("uView", view);
        shader.SetUniform("uProjection", projection);
        shader.SetUniform("uColor", node.MaterialData!.BaseColor);
        shader.SetUniform("uPointSize", data.PointSize);

        GetOrCreate(_pointCache, data, () => new PointMesh(_gl, data)).Draw();
    }

    /// <summary>按材质描述的渲染状态位切换全局 GL 状态（剔除/多边形模式）。</summary>
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

    /// <summary>首次遇到某材质描述时创建 GPU 材质，并按 CPU 引用上传贴图。</summary>
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
        => new(_gl, reference.FilePath, reference.GenerateMipmaps, reference.ColorSpace);

    /// <summary>按引用键取缓存；未命中则由工厂创建并缓存。</summary>
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