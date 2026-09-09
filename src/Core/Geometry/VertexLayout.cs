namespace RobotSimulation.Core.Geometry;

/// <summary>
/// Single source of truth for the interleaved vertex layout (pos3 | uv2 | normal3 | tangent3,
/// 11 floats). Both the CPU side (<see cref="MeshData.ToInterleavedArray"/>) and the rendering
/// backend (RobotSimulation.OpenGL.Mesh vertex attribute pointers) reference these constants so
/// the layout knowledge is not duplicated in two places.
/// </summary>
public static class VertexLayout
{
    /// <summary>Start index of the position component (in floats).</summary>
    public const int PositionFloatOffset = 0;
    public const int PositionComponentCount = 3;

    /// <summary>Start index of the UV component (in floats).</summary>
    public const int UvFloatOffset = PositionFloatOffset + PositionComponentCount;
    public const int UvComponentCount = 2;

    /// <summary>Start index of the normal component (in floats).</summary>
    public const int NormalFloatOffset = UvFloatOffset + UvComponentCount;
    public const int NormalComponentCount = 3;

    /// <summary>Start index of the tangent component (in floats).</summary>
    public const int TangentFloatOffset = NormalFloatOffset + NormalComponentCount;
    public const int TangentComponentCount = 3;

    /// <summary>Number of floats per vertex.</summary>
    public const int FloatsPerVertex = TangentFloatOffset + TangentComponentCount;

    /// <summary>Number of bytes per vertex.</summary>
    public const int BytesPerVertex = FloatsPerVertex * sizeof(float);
}
