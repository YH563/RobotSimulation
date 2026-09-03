using Assimp;
using System.Numerics;
using System.Text.RegularExpressions;
using System.IO;
using RobotSimulation.Core.GameObjects;

namespace RobotSimulation.Core.Utils;

public static class AssimpModelLoader
{
    public static MeshData LoadMesh(string filePath)
    {
        if (!File.Exists(filePath))
            throw new FileNotFoundException($"模型文件不存在: {filePath}");

        string ext = Path.GetExtension(filePath).ToLowerInvariant();
        
    }

    public static MaterialData LoadMaterial(string filePath)
    {
        
    }
    
    
}