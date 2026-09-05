namespace RobotSimulation.Core.Geometry;

/// <summary>
/// 交错顶点布局的单一事实来源（pos3 | uv2 | normal3 | tangent3，共 11 个 float）。
/// CPU 侧（<see cref="MeshData.ToInterleavedArray"/>）与渲染后端
/// （RobotSimulation.OpenGL.Mesh 的顶点属性指针）都应引用本常量，避免两处重复维护布局知识。
/// </summary>
public static class VertexLayout
{
    /// <summary>位置分量起始下标（float 单位）。</summary>
    public const int PositionFloatOffset = 0;
    public const int PositionComponentCount = 3;

    /// <summary>UV 分量起始下标（float 单位）。</summary>
    public const int UvFloatOffset = PositionFloatOffset + PositionComponentCount;
    public const int UvComponentCount = 2;

    /// <summary>法线分量起始下标（float 单位）。</summary>
    public const int NormalFloatOffset = UvFloatOffset + UvComponentCount;
    public const int NormalComponentCount = 3;

    /// <summary>切线分量起始下标（float 单位）。</summary>
    public const int TangentFloatOffset = NormalFloatOffset + NormalComponentCount;
    public const int TangentComponentCount = 3;

    /// <summary>每顶点 float 数。</summary>
    public const int FloatsPerVertex = TangentFloatOffset + TangentComponentCount;

    /// <summary>每顶点字节数。</summary>
    public const int BytesPerVertex = FloatsPerVertex * sizeof(float);
}
