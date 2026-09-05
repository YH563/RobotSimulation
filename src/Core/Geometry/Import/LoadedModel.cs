using System;
using System.Collections.Generic;
using RobotSimulation.Core.Rendering;

namespace RobotSimulation.Core.Geometry.Import;

/// <summary>
/// 模型文件中一个可独立绘制的 submesh：一组几何（MeshData）+ 它绑定的材质（MaterialData）。
/// 全部为纯 CPU 数据，可直接挂到 GameObject 上供渲染器实例化。
/// </summary>
public sealed class LoadedMesh
{
    /// <summary>Assimp 场景中的 mesh 名；文件未命名时为 null。</summary>
    public string? Name { get; }

    /// <summary>几何数据（局部坐标，未做 URDF/Transform 级缩放）。</summary>
    public MeshData MeshData { get; }

    /// <summary>该 submesh 使用的材质（文件自带或默认外观）。</summary>
    public MaterialData MaterialData { get; }

    public LoadedMesh(string? name, MeshData meshData, MaterialData materialData)
    {
        MeshData = meshData ?? throw new ArgumentNullException(nameof(meshData));
        MaterialData = materialData ?? throw new ArgumentNullException(nameof(materialData));
        Name = string.IsNullOrWhiteSpace(name) ? null : name;
    }
}

/// <summary>
/// 一次模型文件导入的完整结果：全部 submesh 平铺为列表，由调用方决定如何挂到 GameObject。
/// </summary>
public sealed class LoadedModel
{
    /// <summary>已解析的模型文件绝对路径。</summary>
    public string FilePath { get; }

    /// <summary>全部可绘制 submesh（空模型在导入时即报错，不会出现空列表）。</summary>
    public IReadOnlyList<LoadedMesh> Meshes { get; }

    public LoadedModel(string filePath, IReadOnlyList<LoadedMesh> meshes)
    {
        FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        Meshes = meshes ?? throw new ArgumentNullException(nameof(meshes));
    }
}
