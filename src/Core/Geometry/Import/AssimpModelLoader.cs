using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using Assimp;
using RobotSimulation.Core.Rendering;
using RobotSimulation.Core.Utils;
// This file's namespace parent is RobotSimulation.Core, so a bare "Scene" would resolve to the
// RobotSimulation.Core.Scene namespace (not the type). Use an alias for Assimp.Scene.
using AScene = Assimp.Scene;

namespace RobotSimulation.Core.Geometry.Import;

/// <summary>
/// 3D model file importer: uses Assimp to parse STL / OBJ / DAE / glTF files into pure CPU data
/// <see cref="LoadedModel"/> (each submesh = <see cref="MeshData"/> + <see cref="MaterialData"/>).
/// No GL/UI dependency; callable from any thread. Textures only record file/memory references;
/// GPU upload is the rendering backend's responsibility.
/// </summary>
public static class AssimpModelLoader
{
    // Pipeline: triangulate + auto UV/normal/tangent space (when missing) + vertex merging + validation
    // + cache-friendly ordering. No FlipUVs/FlipWinding — those are controlled explicitly by LoadOptions.
    private const PostProcessSteps ImportSteps =
        PostProcessSteps.Triangulate |
        PostProcessSteps.GenerateUVCoords |
        PostProcessSteps.GenerateSmoothNormals |
        PostProcessSteps.CalculateTangentSpace |
        PostProcessSteps.JoinIdenticalVertices |
        PostProcessSteps.ValidateDataStructure |
        PostProcessSteps.ImproveCacheLocality;

    /// <summary>Default appearance for material-less files (e.g. STL) — matching the Robot layer's default URDF gray.</summary>
    private static readonly Vector4 DefaultBaseColor = new(0.78f, 0.78f, 0.78f, 1f);

    /// <summary>
    /// Imports an entire model file (one Assimp parse) and returns all submeshes (geometry + material).
    /// </summary>
    /// <param name="filePath">Model file path (absolute or relative).</param>
    /// <param name="options">Import options; null is equivalent to <see cref="LoadOptions.Default"/>.</param>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="ArgumentException">The path is empty.</exception>
    /// <exception cref="IOException">Unsupported format or parse failure (including Assimp native errors).</exception>
    public static LoadedModel Load(string filePath, LoadOptions? options = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("Model file path cannot be empty.", nameof(filePath));

        string fullPath = Path.GetFullPath(filePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Model file not found: {fullPath}", fullPath);

        options ??= LoadOptions.Default;

        AScene scene;
        using (var importer = new AssimpContext())
        {
            scene = importer.ImportFile(fullPath, ImportSteps)
                ?? throw new InvalidOperationException($"Model import failed (Assimp returned an empty scene): {fullPath}");
        }

        string modelDirectory = Path.GetDirectoryName(fullPath) ?? string.Empty;
        var meshes = new List<LoadedMesh>(scene.Meshes?.Count ?? 0);
        if (scene.Meshes is not null)
        {
            foreach (Mesh mesh in scene.Meshes)
            {
                if (mesh.VertexCount <= 0)
                {
                    Logger.Warning($"[Import] Skipping empty mesh '{mesh.Name}' ({fullPath}).");
                    continue;
                }
                meshes.Add(ConvertMesh(scene, mesh, modelDirectory, options));
            }
        }

        if (meshes.Count == 0)
            throw new InvalidOperationException($"The model has no drawable triangle meshes: {fullPath}");

        return new LoadedModel(fullPath, meshes);
    }

    /// <summary>
    /// Convenience entry: loads the geometry of a "single-mesh" model (typical STL / single-mesh OBJ).
    /// Throws when the file has multiple submeshes — use <see cref="Load"/> for the full result.
    /// </summary>
    public static MeshData LoadMeshData(string filePath, LoadOptions? options = null)
    {
        LoadedModel model = Load(filePath, options);
        if (model.Meshes.Count != 1)
            throw new InvalidOperationException(
                $"{model.FilePath} contains {model.Meshes.Count} submeshes; LoadMeshData only supports single-mesh models, use Load instead.");
        return model.Meshes[0].MeshData;
    }

    // ------------------------------------------------------------------
    // Assimp data → Core data
    // ------------------------------------------------------------------

    private static LoadedMesh ConvertMesh(AScene scene, Mesh mesh, string modelDirectory, LoadOptions options)
    {
        var meshData = new MeshData();
        int count = mesh.VertexCount;

        // Defensive handling of missing channels: use compliant placeholders so parallel arrays stay equal in length.
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

        // Vertices are written in order (index i → i); reuse face indices as-is.
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
            return data; // No material (old STL / tool export): default appearance.

        bool hasDiffuseTex = TryFindTexture(material, TextureType.Diffuse, out string? diffusePath);
        bool hasNormalTex = TryFindTexture(material, TextureType.Normals, out string? normalPath)
            || TryFindTexture(material, TextureType.Height, out normalPath);

        // Assimp synthesizes a white "DefaultMaterial" for material-less formats (like STL), which is not
        // the file's real appearance. In that case, with no texture/two-sided flag, treat it as "no valid
        // material" and use default gray rather than pure white.
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

        // Diffuse → Albedo (sRGB); Normal/Height → normal map (Linear).
        if (hasDiffuseTex)
            data.AlbedoTexture = ResolveTexture(scene, diffusePath!, modelDirectory, TextureColorSpace.Srgb);
        if (hasNormalTex)
            data.NormalTexture = ResolveTexture(scene, normalPath!, modelDirectory, TextureColorSpace.Linear);

        return data;
    }

    /// <summary>Fetches the first valid texture slot of the given type. AssimpNet has no count API, so probe until false.</summary>
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
    /// Resolves an Assimp texture reference into a <see cref="TextureReference"/>: embedded textures
    /// (*n) go through FromData; external files resolve relative to the model directory; missing or
    /// unsupported textures only warn and return null.
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

            // Uncompressed embedded textures are raw RGBA pixels with no ready encoding path (the backend
            // decodes from files), so skip them in v1.
            Logger.Warning($"[Import] Model embedded texture '{path}' is uncompressed and not yet supported; using default appearance.");
            return null;
        }

        string fullPath = Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.Combine(modelDirectory, path);

        if (File.Exists(fullPath))
            return TextureReference.FromFile(fullPath, colorSpace);

        Logger.Warning($"[Import] Texture file not found: '{fullPath}', using default appearance.");
        return null;
    }

    /// <summary>Assimp embedded texture references look like "*0", "*1".</summary>
    private static bool IsEmbeddedTexturePath(string path, out int index)
    {
        index = -1;
        if (path.Length < 2 || path[0] != '*')
            return false;
        return int.TryParse(path.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out index);
    }
}
