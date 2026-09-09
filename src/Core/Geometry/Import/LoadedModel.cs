using System;
using System.Collections.Generic;
using RobotSimulation.Core.Rendering;

namespace RobotSimulation.Core.Geometry.Import;

/// <summary>
/// An independently drawable submesh from a model file: a geometry batch (MeshData) plus its bound
/// material (MaterialData). All pure CPU data, attachable to a GameObject for renderer instantiation.
/// </summary>
public sealed class LoadedMesh
{
    /// <summary>Mesh name from the Assimp scene; null when the file does not name it.</summary>
    public string? Name { get; }

    /// <summary>Geometry data (local coordinates, with no URDF/Transform-level scaling).</summary>
    public MeshData MeshData { get; }

    /// <summary>Material used by this submesh (from the file, or a default appearance).</summary>
    public MaterialData MaterialData { get; }

    public LoadedMesh(string? name, MeshData meshData, MaterialData materialData)
    {
        MeshData = meshData ?? throw new ArgumentNullException(nameof(meshData));
        MaterialData = materialData ?? throw new ArgumentNullException(nameof(materialData));
        Name = string.IsNullOrWhiteSpace(name) ? null : name;
    }
}

/// <summary>
/// The full result of one model-file import: all submeshes flattened into a list, for the caller to
/// decide how to attach them to GameObjects.
/// </summary>
public sealed class LoadedModel
{
    /// <summary>Absolute path of the parsed model file.</summary>
    public string FilePath { get; }

    /// <summary>All drawable submeshes (an empty model fails at import, so this is never empty).</summary>
    public IReadOnlyList<LoadedMesh> Meshes { get; }

    public LoadedModel(string filePath, IReadOnlyList<LoadedMesh> meshes)
    {
        FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        Meshes = meshes ?? throw new ArgumentNullException(nameof(meshes));
    }
}
