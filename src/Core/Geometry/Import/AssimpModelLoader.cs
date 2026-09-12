using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Runtime.InteropServices;
using RobotSimulation.Core.Rendering;
using RobotSimulation.Core.Utils;
using Silk.NET.Assimp;
// This file's namespace parent is RobotSimulation.Core, so a bare "Scene" would resolve to the
// RobotSimulation.Core.Scene namespace (not the type). Use an alias for Assimp's scene struct.
using AScene = Silk.NET.Assimp.Scene;
// The bindings also declare a "File" (assimp's aiFile), which would otherwise shadow System.IO.File here.
using File = System.IO.File;

namespace RobotSimulation.Core.Geometry.Import;

/// <summary>
/// 3D model file importer: uses Assimp (through the Silk.NET bindings) to parse STL / OBJ / DAE / glTF / PLY
/// files into pure CPU data <see cref="LoadedModel"/> (each submesh = <see cref="MeshData"/> +
/// <see cref="MaterialData"/>). No GL/UI dependency; callable from any thread. Textures only record
/// file/memory references; GPU upload is the rendering backend's responsibility.
/// </summary>
/// <remarks>
/// This is the one type in the library that reads native memory: Assimp returns a tree of unmanaged structs
/// and everything here is copied out of it (into <see cref="MeshData"/> / <see cref="MaterialData"/>) before
/// the scene is released. The native library arrives with the <c>Silk.NET.Assimp</c> package as per-RID
/// runtime assets, so unlike the earlier <c>AssimpNet</c>-based implementation it needs no separate install and
/// no <c>libdl.so</c> compatibility link — but the binding looks it up by bare name through the OS search path
/// only, so the copy that ships next to the application is mapped explicitly first (<see cref="LoadApi"/>).
/// </remarks>
public static unsafe class AssimpModelLoader
{
    // Pipeline: triangulate + auto UV/normal/tangent space (when missing) + vertex merging + validation
    // + cache-friendly ordering. No FlipUVs/FlipWinding — those are controlled explicitly by LoadOptions.
    private const uint ImportSteps =
        (uint)(PostProcessSteps.Triangulate |
               PostProcessSteps.GenerateUVCoords |
               PostProcessSteps.GenerateSmoothNormals |
               PostProcessSteps.CalculateTangentSpace |
               PostProcessSteps.JoinIdenticalVertices |
               PostProcessSteps.ValidateDataStructure |
               PostProcessSteps.ImproveCacheLocality);

    // Material property keys — the string form of assimp's AI_MATKEY_* macros (key, type 0, index 0).
    private const string MaterialKeyName = "?mat.name";
    private const string MaterialKeyDiffuse = "$clr.diffuse";
    private const string MaterialKeyTwoSided = "$mat.twosided";

    /// <summary>The material name Assimp synthesizes for formats that carry none (STL, PLY); see <see cref="ConvertMaterial"/>.</summary>
    private const string SyntheticDefaultMaterialName = "DefaultMaterial";

    /// <summary>Default appearance for material-less files (e.g. STL) — matching the Robot layer's default URDF gray.</summary>
    private static readonly Vector4 DefaultBaseColor = new(0.78f, 0.78f, 0.78f, 1f);

    /// <summary>
    /// The binding's function table. <c>Assimp.GetApi()</c> hands back one cached instance and locates the
    /// native library on first use, so it is neither per-call nor disposable — holding it statically is
    /// exactly what the binding expects.
    /// </summary>
    private static readonly Assimp Api = LoadApi();

    /// <summary>
    /// Maps the native library that ships with the <c>Silk.NET.Assimp</c> package, then asks the binding for
    /// its function table.
    /// </summary>
    /// <remarks>
    /// The binding resolves the native library by bare name only (<c>libassimp.so.5</c>, <c>Assimp64.dll</c>,
    /// <c>libassimp.5.dylib</c>) and lets the OS search path decide — it never looks next to the application,
    /// which is where NuGet puts the per-RID runtime assets of a framework-dependent build
    /// (<c>runtimes/&lt;rid&gt;/native</c>). On a machine that happens to have Assimp installed system-wide the
    /// bare name resolves and the package's own copy goes unused; on a machine that does not (a bare CI
    /// runner, a container, a clean user PC) every name fails with "Could not load from any of the possible
    /// library names", even though the file it wants is sitting in the application directory. Mapping the
    /// shipped file by absolute path first makes the outcome independent of the OS search path.
    /// </remarks>
    private static Assimp LoadApi()
    {
        TryLoadBundledNative();
        return Assimp.GetApi();
    }

    /// <summary>
    /// Preloads the bundled native library when one was published next to the application. Never throws: if
    /// nothing is found, or the candidate is found but cannot be mapped, the binding's own search still runs.
    /// </summary>
    private static void TryLoadBundledNative()
    {
        // Only the names the binding itself asks for: those are the ABI its function table was generated
        // against, and a library with any other soname would not satisfy the binding's own lookup anyway.
        string[] names;
        if (OperatingSystem.IsWindows())
            names = [Environment.Is64BitProcess ? "Assimp64.dll" : "Assimp32.dll"];
        else if (OperatingSystem.IsMacOS())
            names = ["libassimp.5.dylib"];
        else
            names = ["libassimp.so.5"];

        try
        {
            foreach (string path in EnumerateBundledNativePaths(names))
            {
                if (NativeLibrary.TryLoad(path, out _))
                {
                    // The mapping deliberately outlives this call: it stays in the process for the binding.
                    Logger.Debug($"[Import] Loaded the Assimp native library bundled with the package: {path}");
                    return;
                }

                Logger.Warning($"[Import] The bundled Assimp native library could not be loaded: {path}");
            }

            Logger.Debug("[Import] No bundled Assimp native library next to the application; "
                         + "the Silk.NET binding will search the OS library directories instead.");
        }
        catch (Exception ex)
        {
            // Probing the application directory must never be the reason a model import fails.
            Logger.Warning($"[Import] Could not probe the application directory for the bundled Assimp native library: {ex.Message}");
        }
    }

    /// <summary>
    /// Yields the places a framework-dependent build keeps the package's per-RID runtime assets, most likely
    /// first: <c>runtimes/&lt;runtime identifier&gt;/native/&lt;name&gt;</c> under the application directory, then the
    /// same file name in any other RID directory published alongside it.
    /// </summary>
    /// <param name="names">The file names the binding asks for, in its own preference order.</param>
    /// <remarks>
    /// The extra RID directories cover hosts whose output carries a differently spelled RID folder
    /// (self-contained or distro-specific) next to the portable one the runtime reports. Loading is the filter
    /// for those: a file built for another OS or architecture simply fails to load and the loop moves on.
    /// </remarks>
    private static IEnumerable<string> EnumerateBundledNativePaths(string[] names)
    {
        string runtimesRoot = Path.Combine(AppContext.BaseDirectory, "runtimes");
        if (!Directory.Exists(runtimesRoot))
            yield break;

        string reportedNativeDirectory = Path.Combine(runtimesRoot, RuntimeInformation.RuntimeIdentifier, "native");
        foreach (string name in names)
        {
            string path = Path.Combine(reportedNativeDirectory, name);
            if (File.Exists(path))
                yield return path;
        }

        foreach (string nativeDirectory in Directory.EnumerateDirectories(runtimesRoot, "native", SearchOption.AllDirectories))
        {
            if (string.Equals(nativeDirectory, reportedNativeDirectory, StringComparison.OrdinalIgnoreCase))
                continue;

            foreach (string name in names)
            {
                string path = Path.Combine(nativeDirectory, name);
                if (File.Exists(path))
                    yield return path;
            }
        }
    }

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

        string modelDirectory = Path.GetDirectoryName(fullPath) ?? string.Empty;

        AScene* scene = Api.ImportFile(fullPath, ImportSteps);
        if (scene is null)
            throw new InvalidOperationException(
                $"Model import failed (Assimp returned an empty scene): {fullPath}. {Api.GetErrorStringS()}");

        var meshes = new List<LoadedMesh>((int)scene->MNumMeshes);
        try
        {
            for (uint i = 0; i < scene->MNumMeshes; i++)
            {
                Mesh* mesh = scene->MMeshes[i];
                if (mesh->MNumVertices == 0)
                {
                    Logger.Warning($"[Import] Skipping empty mesh '{mesh->MName.AsString}' ({fullPath}).");
                    continue;
                }

                meshes.Add(ConvertMesh(scene, mesh, modelDirectory, options));
            }
        }
        finally
        {
            // Everything above was already copied into MeshData / MaterialData; the native tree is freed
            // here, and the finally keeps a throw mid-loop from leaking it.
            Api.ReleaseImport(scene);
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

    private static LoadedMesh ConvertMesh(AScene* scene, Mesh* mesh, string modelDirectory, LoadOptions options)
    {
        var meshData = new MeshData();
        int count = (int)mesh->MNumVertices;

        // Defensive handling of missing channels: every array below is optional in assimp, so a missing one
        // falls back to a placeholder and the parallel arrays stay equal in length.
        bool hasNormals = mesh->MNormals is not null;
        bool hasTangents = mesh->MTangents is not null;
        Vector3* uv0 = mesh->MTextureCoords[0];
        bool hasUvs = uv0 is not null;

        float scale = options.GlobalScale ?? 1f;

        for (int i = 0; i < count; i++)
        {
            Vector3 p = mesh->MVertices[i];
            var position = scale == 1f ? p : p * scale;

            Vector3 normal = hasNormals ? mesh->MNormals[i] : Vector3.UnitZ;
            Vector2 uv = hasUvs ? new Vector2(uv0[i].X, options.FlipUvV ? 1f - uv0[i].Y : uv0[i].Y) : Vector2.Zero;
            Vector3 tangent = hasTangents ? mesh->MTangents[i] : Vector3.UnitX;

            meshData.AddVertex(position, normal, uv, tangent);
        }

        // Vertices are written in order (index i → i); reuse face indices as-is.
        for (uint f = 0; f < mesh->MNumFaces; f++)
        {
            Face face = mesh->MFaces[f];
            if (face.MIndices is null || face.MNumIndices < 3)
                continue;

            uint a = face.MIndices[0];
            uint b = face.MIndices[1];
            uint c = face.MIndices[2];
            if (options.FlipWinding)
                (b, c) = (c, b);
            meshData.AddTriangle(a, b, c);
        }

        return new LoadedMesh(mesh->MName.AsString, meshData, ConvertMaterial(scene, mesh, modelDirectory));
    }

    private static MaterialData ConvertMaterial(AScene* scene, Mesh* mesh, string modelDirectory)
    {
        var data = new MaterialData { BaseColor = DefaultBaseColor };

        Material* material = mesh->MMaterialIndex < scene->MNumMaterials
            ? scene->MMaterials[mesh->MMaterialIndex]
            : null;

        if (material is null)
            return data; // No material (old STL / tool export): default appearance.

        bool hasDiffuseTex = TryFindTexture(material, TextureType.Diffuse, out string? diffusePath);
        bool hasNormalTex = TryFindTexture(material, TextureType.Normals, out string? normalPath)
            || TryFindTexture(material, TextureType.Height, out normalPath);

        // Assimp synthesizes a white "DefaultMaterial" for material-less formats (like STL), which is not
        // the file's real appearance. In that case, with no texture/two-sided flag, treat it as "no valid
        // material" and use default gray rather than pure white.
        bool isSyntheticDefault =
            string.Equals(ReadMaterialName(material), SyntheticDefaultMaterialName, StringComparison.Ordinal)
            && !hasDiffuseTex && !hasNormalTex
            && !IsTwoSided(material);

        if (!isSyntheticDefault)
        {
            if (TryGetDiffuseColor(material, out Vector4 color))
                data.BaseColor = color;

            if (IsTwoSided(material))
                data.DoubleSided = true;
        }

        // Diffuse → Albedo (sRGB); Normal/Height → normal map (Linear).
        if (hasDiffuseTex)
            data.AlbedoTexture = ResolveTexture(scene, diffusePath!, modelDirectory, TextureColorSpace.Srgb);
        if (hasNormalTex)
            data.NormalTexture = ResolveTexture(scene, normalPath!, modelDirectory, TextureColorSpace.Linear);

        return data;
    }

    /// <summary>Reads <c>AI_MATKEY_NAME</c>; null when the file itself names no material.</summary>
    private static string? ReadMaterialName(Material* material)
    {
        AssimpString name = default;
        return Api.GetMaterialString(material, MaterialKeyName, 0, 0, &name) == Return.Success ? name.AsString : null;
    }

    /// <summary>Reads <c>AI_MATKEY_COLOR_DIFFUSE</c> (assimp accepts three or four components).</summary>
    private static bool TryGetDiffuseColor(Material* material, out Vector4 color)
    {
        // A ref/out parameter is not a fixed variable, so the address is taken through a local.
        Vector4 value = default;
        bool found = Api.GetMaterialColor(material, MaterialKeyDiffuse, 0, 0, &value) == Return.Success;
        color = value;
        return found;
    }

    /// <summary>
    /// Reads <c>AI_MATKEY_TWOSIDED</c>. Non-zero means "on": assimp stores the flag as a plain int and
    /// importers write either 1 or -1 for true, so only 0 counts as single-sided.
    /// </summary>
    private static bool IsTwoSided(Material* material)
    {
        int value = 0;
        uint componentCount = 0;
        return Api.GetMaterialIntegerArray(material, MaterialKeyTwoSided, 0, 0, &value, &componentCount) == Return.Success
               && value != 0;
    }

    /// <summary>Fetches the first texture of the given type that names a path. Assimp has no count API, so probe until it fails.</summary>
    private static bool TryFindTexture(Material* material, TextureType type, out string? filePath)
    {
        filePath = null;
        for (uint i = 0; ; i++)
        {
            AssimpString path = default;
            TextureMapping mapping = default;
            uint uvIndex = 0;
            float blend = 0;
            TextureOp op = default;
            TextureMapMode mapMode = default;
            uint flags = 0;

            if (Api.GetMaterialTexture(material, type, i, &path, &mapping, &uvIndex, &blend, &op, &mapMode, &flags)
                != Return.Success)
                break;

            if (!string.IsNullOrWhiteSpace(path.AsString))
            {
                filePath = path.AsString;
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
        AScene* scene, string path, string modelDirectory, TextureColorSpace colorSpace)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        if (IsEmbeddedTexturePath(path, out int index)
            && scene->MTextures is not null
            && index >= 0 && index < scene->MNumTextures)
        {
            Texture* embedded = scene->MTextures[index];
            if (embedded->PcData is not null && embedded->MWidth > 0)
            {
                // A compressed embedded texture is stored as the encoded file itself: assimp puts the byte
                // length in mWidth and leaves mHeight at 0. Copy the bytes out (nothing may outlive the
                // scene) and let the backend decode them exactly as it decodes an external file.
                if (embedded->MHeight == 0)
                {
                    byte[] bytes = new ReadOnlySpan<byte>((byte*)embedded->PcData, (int)embedded->MWidth).ToArray();
                    return TextureReference.FromData(bytes, colorSpace);
                }
            }

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
