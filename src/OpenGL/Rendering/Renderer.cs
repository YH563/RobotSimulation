using System;
using System.Collections.Generic;
using System.Numerics;
using RobotSimulation.Core.GameObjects;
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
    private readonly ShaderProgram _standardShader;
    private readonly Dictionary<MeshData, Mesh> _meshCache = new();
    private readonly Dictionary<MaterialData, Material> _materialCache = new();
    private bool _disposed;

    /// <param name="device">渲染上下文（设备层入口），由 Host 组装。</param>
    /// <param name="vertexShaderPath">默认着色器顶点路径。</param>
    /// <param name="fragmentShaderPath">默认着色器片元路径。</param>
    public Renderer(GraphicsContext device, string vertexShaderPath, string fragmentShaderPath)
    {
        ArgumentNullException.ThrowIfNull(device);
        _gl = device.NativeGl;
        _standardShader = device.LoadShader(vertexShaderPath, fragmentShaderPath);
    }

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
        _standardShader.SetUniform("uLightCount", count);
        _standardShader.SetUniform("uLightColors", colors);
        _standardShader.SetUniform("uLightPositions", positions);
        _standardShader.SetUniform("uLightDirections", directions);
        _standardShader.SetUniform("uLightTypes", types);
        _standardShader.SetUniform("uLightIntensities", intensities);
    }

    private void RenderNode(GameObject node, SceneGraph scene, Matrix4x4 view, Matrix4x4 projection)
    {
        // 携带数据且可见的节点：实例化 GPU 资源并绘制。
        // 纯骨架/层级节点（无数据）只作为父节点递归，不阻断子树（URDF link 常如此）。
        if (node.Visible && node.MeshData != null && node.MaterialData != null)
        {
            Mesh mesh = GetOrCreateMesh(node.MeshData);
            Material material = GetOrCreateMaterial(node.MaterialData);

            // 渲染状态位：双面（关闭背面剔除）/ 线框，绘制前一次性设置
            ApplyRenderState(node.MaterialData);

            // 外观参数直接来自 CPU 描述（无镜像副本，无需手动同步）
            material.Apply(node.MaterialData);
            var shader = material.Shader;
            shader.SetUniform("uModel", node.Transform.GetModelMatrix());
            shader.SetUniform("uView", view);
            shader.SetUniform("uProjection", projection);
            shader.SetUniform("uViewPos", scene.Camera.Position);

            mesh.Draw();
        }

        foreach (Transform child in node.Transform.Children)
            RenderNode(child.Owner, scene, view, projection);
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
        var material = new Material(_standardShader);
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
        DisposeAll(_materialCache.Values);
        _disposed = true;
    }

    private static void DisposeAll<T>(IEnumerable<T> resources)
        where T : IDisposable
    {
        foreach (T resource in resources)
            resource.Dispose();
    }
}
