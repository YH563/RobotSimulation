using System;
using System.IO;
using System.Linq;
using System.Reflection;
using RobotSimulation.Core.Rendering;

namespace RobotSimulation.OpenGL.Resources;

/// <summary>
/// 标准着色器目录：GLSL 源文件位于本程序集 <c>Shaders/</c> 目录
/// （Model/Line/Point/Skybox 各自的 .vert/.frag），以嵌入式资源随
/// RobotSimulation.OpenGL 分发。Core 侧只约定 <see cref="RenderPassKind"/> 通道，
/// 宿主无需管理 shader 文件路径；编辑 GLSL 直接改 <c>Shaders/*.vert|frag</c> 即可。
/// </summary>
public static class EmbeddedShaders
{
    private static readonly Assembly Self = typeof(EmbeddedShaders).Assembly;

    /// <summary>按通道取 (顶点, 片段) 源码。</summary>
    public static (string Vertex, string Fragment) Get(RenderPassKind pass) => pass switch
    {
        RenderPassKind.Model => (Read("Model.vert"), Read("Model.frag")),
        RenderPassKind.Line => (Read("Line.vert"), Read("Line.frag")),
        RenderPassKind.Point => (Read("Point.vert"), Read("Point.frag")),
        RenderPassKind.Skybox => (Read("Skybox.vert"), Read("Skybox.frag")),
        RenderPassKind.Axes => (Read("Axes.vert"), Read("Axes.frag")),
        _ => throw new ArgumentOutOfRangeException(nameof(pass)),
    };

    /// <summary>读取嵌入的 GLSL 文本（资源名以 ".Shaders.{fileName}" 结尾，忽略程序集根命名空间差异）。</summary>
    private static string Read(string fileName)
    {
        string suffix = $".Shaders.{fileName}";
        string? resourceName = Self.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(suffix, StringComparison.Ordinal));

        if (resourceName is null)
            throw new FileNotFoundException(
                $"找不到内嵌着色器资源 '{suffix}'。" +
                "请确认 RobotSimulation.OpenGL 工程的 Shaders/ 目录下的 .vert/.frag 已被包含为 EmbeddedResource。");

        using Stream stream = Self.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
