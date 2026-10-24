using System;
using System.Collections.Generic;
using System.Numerics;

namespace RobotSimulation.Core.Geometry.Collision;

/// <summary>
/// Immutable median-split BVH over a <see cref="MeshData"/>'s triangles. Built by <see cref="MeshShape"/>
/// and cached until the mesh revision changes. Nodes and the triangle order are exposed internally so the
/// narrow phase can traverse two trees together without copying vertex data.
/// </summary>
internal sealed class TriangleBvh
{
    private const int LeafSize = 4;

    private TriangleBvh(MeshData mesh, Bounds bounds, Node[] nodes, int[] triangles)
    {
        Mesh = mesh;
        Bounds = bounds;
        Nodes = nodes;
        Triangles = triangles;
    }

    /// <summary>Source mesh (referenced, not copied).</summary>
    public MeshData Mesh { get; }

    /// <summary>Local-space bounds of all triangles; an empty mesh yields Min = Max = 0.</summary>
    public Bounds Bounds { get; }

    /// <summary>BVH nodes; index 0 is the root (when <see cref="IsEmpty"/> is false).</summary>
    public Node[] Nodes { get; }

    /// <summary>Triangle indices (into <see cref="MeshData.TriangleCount"/>), grouped by leaf ranges.</summary>
    public int[] Triangles { get; }

    /// <summary>Whether the source mesh has no triangles.</summary>
    public bool IsEmpty => Triangles.Length == 0;

    /// <summary>Builds a BVH for <paramref name="mesh"/>.</summary>
    public static TriangleBvh Build(MeshData mesh)
    {
        if (mesh is null)
            throw new ArgumentNullException(nameof(mesh));

        int triangleCount = mesh.TriangleCount;
        if (triangleCount == 0)
            return new TriangleBvh(mesh, new Bounds(Vector3.Zero, Vector3.Zero), Array.Empty<Node>(),
                Array.Empty<int>());

        var triangles = new int[triangleCount];
        for (int i = 0; i < triangleCount; i++)
            triangles[i] = i;

        var nodes = new List<Node>(Math.Max(1, triangleCount / LeafSize * 2));
        int root = BuildNode(mesh, triangles, nodes, 0, triangleCount);
        return new TriangleBvh(mesh, nodes[root].Bounds, nodes.ToArray(), triangles);
    }

    /// <summary>Gets the three vertices of the triangle at a position in <see cref="Triangles"/>.</summary>
    public void GetTriangle(int orderIndex, out Vector3 a, out Vector3 b, out Vector3 c)
    {
        int triangle = Triangles[orderIndex];
        int i = triangle * 3;
        IReadOnlyList<uint> indices = Mesh.Indices;
        IReadOnlyList<Vector3> positions = Mesh.Positions;
        a = positions[(int)indices[i]];
        b = positions[(int)indices[i + 1]];
        c = positions[(int)indices[i + 2]];
    }

    private static int BuildNode(MeshData mesh, int[] triangles, List<Node> nodes, int start, int count)
    {
        Bounds bounds = ComputeBounds(mesh, triangles, start, count);
        int nodeIndex = nodes.Count;
        nodes.Add(default);

        if (count <= LeafSize)
        {
            nodes[nodeIndex] = new Node(bounds, start, count, -1, -1);
            return nodeIndex;
        }

        Vector3 size = bounds.Size;
        int axis = size.X >= size.Y && size.X >= size.Z ? 0 : size.Y >= size.Z ? 1 : 2;

        int middle = start + count / 2;
        Array.Sort(triangles, start, count, new CentroidComparer(mesh, axis));

        int left = BuildNode(mesh, triangles, nodes, start, middle - start);
        int right = BuildNode(mesh, triangles, nodes, middle, start + count - middle);
        nodes[nodeIndex] = new Node(bounds, start, 0, left, right);
        return nodeIndex;
    }

    private static Bounds ComputeBounds(MeshData mesh, int[] triangles, int start, int count)
    {
        Vector3 min = new(float.PositiveInfinity);
        Vector3 max = new(float.NegativeInfinity);
        IReadOnlyList<Vector3> positions = mesh.Positions;
        IReadOnlyList<uint> indices = mesh.Indices;

        for (int i = start; i < start + count; i++)
        {
            int index = triangles[i] * 3;
            Include(positions[(int)indices[index]], ref min, ref max);
            Include(positions[(int)indices[index + 1]], ref min, ref max);
            Include(positions[(int)indices[index + 2]], ref min, ref max);
        }

        return new Bounds(min, max);
    }

    private static void Include(Vector3 point, ref Vector3 min, ref Vector3 max)
    {
        min = Vector3.Min(min, point);
        max = Vector3.Max(max, point);
    }

    private static Vector3 Centroid(MeshData mesh, int triangle)
    {
        int i = triangle * 3;
        IReadOnlyList<uint> indices = mesh.Indices;
        IReadOnlyList<Vector3> positions = mesh.Positions;
        return (positions[(int)indices[i]] + positions[(int)indices[i + 1]] + positions[(int)indices[i + 2]]) / 3f;
    }

    /// <summary>A BVH node: a leaf when <see cref="Count"/> &gt; 0, otherwise an internal node with child indices.</summary>
    internal readonly struct Node
    {
        public Node(Bounds bounds, int start, int count, int left, int right)
        {
            Bounds = bounds;
            Start = start;
            Count = count;
            Left = left;
            Right = right;
        }

        public Bounds Bounds { get; }
        public int Start { get; }
        public int Count { get; }
        public int Left { get; }
        public int Right { get; }
        public bool IsLeaf => Count > 0;
    }

    private sealed class CentroidComparer : IComparer<int>
    {
        private readonly MeshData _mesh;
        private readonly int _axis;

        public CentroidComparer(MeshData mesh, int axis)
        {
            _mesh = mesh;
            _axis = axis;
        }

        public int Compare(int x, int y)
        {
            float cx = Centroid(_mesh, x)[_axis];
            float cy = Centroid(_mesh, y)[_axis];
            return cx.CompareTo(cy);
        }
    }
}
