using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using Assimp;
using RobotSimulation.Core.Rendering;
using RobotSimulation.Core.Utils;
// 本文件命名空间上级是 RobotSimulation.Core，裸写 Scene 会命中 RobotSimulation.Core.Scene
// 命名空间（而非类型），故对 Assimp.Scene 用别名。
using AScene = Assimp.Scene;

namespace RobotSimulation.Core.Geometry.Import;

/// <summary>
/// 3D 模型文件导入器：用 Assimp 把 STL / OBJ / DAE / glTF 等文件解析为纯 CPU 数据
/// <see cref="LoadedModel"/>（每个 submesh = <see cref="MeshData"/> + <see cref="MaterialData"/>）。
/// 不依赖 GL / UI，可在任意线程调用；贴图只记录文件/内存引用，GPU 上传归渲染后端。
/// </summary>
public static class AssimpModelLoader
{
    // 管线选择：三角化 + 自动 UV/法线/切空间（缺失时）+ 顶点合并 + 校验 + 缓存友好。
    // 不做 FlipUVs/FlipWinding——由 LoadOptions 显式控制，行为可预期。
    private const PostProcessSteps ImportSteps =
        PostProcessSteps.Triangulate |
        PostProcessSteps.GenerateUVCoords |
        PostProcessSteps.GenerateSmoothNormals |
        PostProcessSteps.CalculateTangentSpace |
        PostProcessSteps.JoinIdenticalVertices |
        PostProcessSteps.ValidateDataStructure |
        PostProcessSteps.ImproveCacheLocality;

    /// <summary>无材质文件（如 STL）的默认外观——与 Robot 层 URDF 默认灰一致。</summary>
    private static readonly Vector4 DefaultBaseColor = new(0.78f, 0.78f, 0.78f, 1f);

    /// <summary>
    /// 导入整个模型文件（一次 Assimp 解析），返回全部 submesh（几何 + 材质）。
    /// </summary>
    /// <param name="filePath">模型文件路径（绝对或相对）。</param>
    /// <param name="options">导入选项；null 等价于 <see cref="LoadOptions.Default"/>。</param>
    /// <exception cref="FileNotFoundException">文件不存在。</exception>
    /// <exception cref="ArgumentException">路径为空。</exception>
    /// <exception cref="IOException">格式不支持或解析失败（含 Assimp 原生异常）。</exception>
    public static LoadedModel Load(string filePath, LoadOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("模型文件路径不能为空。", nameof(filePath));

        string fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"模型文件不存在: {fullPath}", fullPath);

        options ??= LoadOptions.Default;

        AScene scene;
        using (var importer = new AssimpContext())
        {
            scene = importer.ImportFile(fullPath, ImportSteps)
                ?? throw new InvalidOperationException($"模型导入失败（Assimp 返回空场景）：{fullPath}");
        }

        string modelDirectory = Path.GetDirectoryName(fullPath) ?? string.Empty;
        var meshes = new List<LoadedMesh>(scene.Meshes?.Count ?? 0);
        if (scene.Meshes is not null)
        {
            foreach (Mesh mesh in scene.Meshes)
            {
                if (mesh.VertexCount <= 0)
                {
                    Logger.Warning($"[Import] 跳过空 mesh '{mesh.Name}'（{fullPath}）。");
                    continue;
                }
                meshes.Add(ConvertMesh(scene, mesh, modelDirectory, options));
            }
        }

        if (meshes.Count == 0)
            throw new InvalidOperationException($"模型不包含可绘制的三角网格：{fullPath}");

        return new LoadedModel(fullPath, meshes);
    }

    /// <summary>
    /// 便捷入口：加载“仅含一个 mesh”模型的几何数据（典型 STL / 单 mesh OBJ）。
    /// 文件含多个 submesh 时抛异常——请改用 <see cref="Load"/> 获取完整结果。
    /// </summary>
    public static MeshData LoadMeshData(string filePath, LoadOptions? options = null)
    {
        LoadedModel model = Load(filePath, options);
        if (model.Meshes.Count != 1)
            throw new InvalidOperationException(
                $"{model.FilePath} 含 {model.Meshes.Count} 个 submesh；LoadMeshData 仅适用于单 mesh 模型，请改用 Load。");
        return model.Meshes[0].MeshData;
    }

    // ------------------------------------------------------------------
    // Assimp 数据 → Core 数据
    // ------------------------------------------------------------------

    private static LoadedMesh ConvertMesh(AScene scene, Mesh mesh, string modelDirectory, LoadOptions options)
    {
        var meshData = new MeshData();
        int count = mesh.VertexCount;

        // 缺失通道防御：顶点缺的通道用合规占位值，保证并行数组长度一致
        bool hasNormals = mesh.HasNormals && mesh.Normals is { Count: > 0 } && mesh.Normals.Count == count;
        bool hasTangents = mesh.HasTangentBasis && mesh.Tangents is { Count: > 0 } && mesh.Tangents.Count == count;
        List<Vector3D>? uvChannel =
            mesh.TextureCoordinateChannels is { Length: > 0 } ? mesh.TextureCoordinateChannels[0] : null;
        bool hasUvs = uvChannel is { Count: > 0 } && uvChannel.Count == count;

        float scale = options.GlobalScale ?? 1f;

        for (int i = 0; i < count; i++)
        {
            Vector3D p = mesh.Vertices[i];
            var position = scale == 1f
                ? new Vector3(p.X, p.Y, p.Z)
                : new Vector3(p.X * scale, p.Y * scale, p.Z * scale);

            Vector3 normal = hasNormals
                ? new Vector3(mesh.Normals[i].X, mesh.Normals[i].Y, mesh.Normals[i].Z)
                : Vector3.UnitZ;

            Vector2 uv = hasUvs && uvChannel is { } uv0
                ? new Vector2(uv0[i].X, options.FlipUvV ? 1f - uv0[i].Y : uv0[i].Y)
                : Vector2.Zero;

            Vector3 tangent = hasTangents
                ? new Vector3(mesh.Tangents[i].X, mesh.Tangents[i].Y, mesh.Tangents[i].Z)
                : Vector3.UnitX;

            meshData.AddVertex(position, normal, uv, tangent);
        }

        // 顶点按顺序写入 MeshData（索引 i → i），面索引直接沿用即可
        foreach (Face face in mesh.Faces)
        {
            if (!face.HasIndices || face.IndexCount < 3)
                continue;

            uint a = (uint)face.Indices[0];
            uint b = (uint)face.Indices[1];
            uint c = (uint)face.Indices[2];
            if (options.FlipWinding)
                (b, c) = (c, b);
            meshData.AddTriangle(a, b, c);
        }

        return new LoadedMesh(mesh.Name, meshData, ConvertMaterial(scene, mesh, modelDirectory));
    }

    private static MaterialData ConvertMaterial(AScene scene, Mesh mesh, string modelDirectory)
    {
        var data = new MaterialData { BaseColor = DefaultBaseColor };

        Material? material = null;
        if (scene.Materials is not null
            && mesh.MaterialIndex >= 0
            && mesh.MaterialIndex < scene.Materials.Count)
        {
            material = scene.Materials[mesh.MaterialIndex];
        }

        if (material is null)
            return data; // 无材质（老 STL/工具导出）：默认外观

        bool hasDiffuseTex = TryFindTexture(material, TextureType.Diffuse, out string? diffusePath);
        bool hasNormalTex = TryFindTexture(material, TextureType.Normals, out string? normalPath)
            || TryFindTexture(material, TextureType.Height, out normalPath);

        // Assimp 对无材质格式（如 STL）会合成白色 "DefaultMaterial"——不是文件的真实外观。
        // 此时若文件没带任何贴图/双面标记，视为"无有效材质"，用默认灰而非纯白。
        bool isSyntheticDefault = string.Equals(material.Name, "DefaultMaterial", StringComparison.Ordinal)
            && !hasDiffuseTex && !hasNormalTex
            && !(material.HasTwoSided && material.IsTwoSided);

        if (!isSyntheticDefault)
        {
            if (material.HasColorDiffuse)
            {
                Color4D c = material.ColorDiffuse;
                data.BaseColor = new Vector4(c.R, c.G, c.B, c.A);
            }

            if (material.HasTwoSided && material.IsTwoSided)
                data.DoubleSided = true;
        }

        // Diffuse → Albedo（sRGB）；Normal/Height → 法线贴图（Linear）
        if (hasDiffuseTex)
            data.AlbedoTexture = ResolveTexture(scene, diffusePath!, modelDirectory, TextureColorSpace.Srgb);
        if (hasNormalTex)
            data.NormalTexture = ResolveTexture(scene, normalPath!, modelDirectory, TextureColorSpace.Linear);

        return data;
    }

    /// <summary>取指定类型的第一个有效贴图槽。AssimpNet 无计数 API，循环探测到 false 为止。</summary>
    private static bool TryFindTexture(Material material, TextureType type, out string? filePath)
    {
        filePath = null;
        for (int i = 0; ; i++)
        {
            if (!material.GetMaterialTexture(type, i, out TextureSlot slot))
                break;
            if (!string.IsNullOrWhiteSpace(slot.FilePath))
            {
                filePath = slot.FilePath;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 把 Assimp 贴图引用解析为 <see cref="TextureReference"/>：
    /// 内嵌贴图（*n）走 FromData；外部文件按模型文件目录解析相对路径；缺失/不支持仅告警并返回 null。
    /// </summary>
    private static TextureReference? ResolveTexture(
        AScene scene, string path, string modelDirectory, TextureColorSpace colorSpace)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        if (IsEmbeddedTexturePath(path, out int index)
            && scene.Textures is { Count: > 0 } textures
            && index >= 0 && index < textures.Count)
        {
            EmbeddedTexture embedded = textures[index];
            if (embedded.HasCompressedData && embedded.CompressedData is { Length: > 0 })
                return TextureReference.FromData(embedded.CompressedData, colorSpace);

            // 未压缩内嵌贴图是裸 RGBA 像素，无现成编码路径（后端按文件解码），v1 跳过
            Logger.Warning($"[Import] 模型内嵌贴图 '{path}' 为未压缩格式，暂不支持，使用默认外观。");
            return null;
        }

        string fullPath = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.Combine(modelDirectory, path);

        if (File.Exists(fullPath))
            return TextureReference.FromFile(fullPath, colorSpace);

        Logger.Warning($"[Import] 找不到贴图文件 '{fullPath}'，使用默认外观。");
        return null;
    }

    /// <summary>Assimp 内嵌贴图引用形如 "*0"、"*1"。</summary>
    private static bool IsEmbeddedTexturePath(string path, out int index)
    {
        index = -1;
        if (path.Length < 2 || path[0] != '*')
            return false;
        return int.TryParse(path.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out index);
    }
}
