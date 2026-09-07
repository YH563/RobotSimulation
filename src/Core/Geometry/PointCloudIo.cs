using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace RobotSimulation.Core.Geometry;

/// <summary>
/// Loads point clouds from common file formats into <see cref="PointCloud2Data"/>.
/// Supported: PCL PCD (ascii + binary) and Stanford PLY (ascii + binary_little_endian).
/// PCD <c>binary_compressed</c> is not supported and throws <see cref="NotSupportedException"/>.
/// </summary>
public static class PointCloudIo
{
    /// <summary>Loads a point cloud file, choosing a parser by file extension (.pcd / .ply).</summary>
    public static PointCloud2Data Load(string path, string? frameId = null)
    {
        if (path is null)
            throw new ArgumentNullException(nameof(path));

        string ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".pcd" => ReadPcd(path, frameId),
            ".ply" => ReadPly(path, frameId),
            _ => throw new NotSupportedException($"Unsupported point cloud format '{ext}'. Supported: .pcd, .ply."),
        };
    }

    /// <summary>Reads a PCL PCD file (ascii or binary).</summary>
    public static PointCloud2Data ReadPcd(string path, string? frameId = null)
    {
        if (path is null)
            throw new ArgumentNullException(nameof(path));

        byte[] bytes = File.ReadAllBytes(path);
        return ParsePcd(bytes, frameId, label: path);
    }

    /// <summary>Parses a PCD file from its raw bytes (header is text, payload may be binary).</summary>
    public static PointCloud2Data ParsePcd(byte[] bytes, string? frameId = null, string? label = null)
    {
        if (bytes is null)
            throw new ArgumentNullException(nameof(bytes));

        // PCD header is ASCII, so a string index matches the byte offset up to the DATA line.
        string text = Encoding.ASCII.GetString(bytes);
        PcdHeader header = ParsePcdHeader(text, label);

        byte[] data;
        if (header.Format == "ascii")
        {
            var dataBytes = new byte[header.Points * header.PointStep];
            FillPcdAscii(text, header, dataBytes, label);
            data = dataBytes;
        }
        else if (header.Format == "binary")
        {
            if (header.DataStart + header.Points * header.PointStep > bytes.Length)
                throw new InvalidDataException($"{label ?? "<pcd>"}: binary payload is shorter than declared POINTS * pointStep.");
            data = new byte[header.Points * header.PointStep];
            Array.Copy(bytes, header.DataStart, data, 0, data.Length);
        }
        else if (header.Format == "binary_compressed")
        {
            throw new NotSupportedException($"{label ?? "<pcd>"}: PCD binary_compressed is not supported.");
        }
        else
        {
            throw new InvalidDataException($"{label ?? "<pcd>"}: unknown DATA format '{header.Format}'.");
        }

        return new PointCloud2Data(header.Fields.ToArray(), data, header.PointStep,
            frameId, width: header.Width, height: header.Height > 0 ? header.Height : 1);
    }


    private sealed class PcdHeader
    {
        public List<string> Names = new();
        public List<int> Sizes = new();
        public List<char> Types = new();
        public List<int> Counts = new();
        public int Width;
        public int Height = 1;
        public int Points;
        public int PointStep;
        public string Format = "";
        public int DataStart;
        public List<PointField> Fields = new();
    }

    private static PcdHeader ParsePcdHeader(string text, string? label)
    {
        var h = new PcdHeader();
        bool sawFields = false, sawSizes = false, sawTypes = false;
        int pos = 0;
        while (pos < text.Length)
        {
            int nl = text.IndexOf('\n', pos);
            string line = text[pos..(nl < 0 ? text.Length : nl)].TrimEnd('\r');
            int next = nl < 0 ? text.Length : nl + 1;

            if (line.Length == 0 || line.StartsWith('#'))
            {
                pos = next;
                continue;
            }

            if (line.StartsWith("DATA"))
            {
                h.Format = line.Substring(4).Trim().ToLowerInvariant();
                h.DataStart = next;
                break;
            }

            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (parts[0])
            {
                case "FIELDS":
                    for (int i = 1; i < parts.Length; i++) h.Names.Add(parts[i]);
                    sawFields = true;
                    break;
                case "SIZE":
                    for (int i = 1; i < parts.Length; i++) h.Sizes.Add(int.Parse(parts[i], CultureInfo.InvariantCulture));
                    sawSizes = true;
                    break;
                case "TYPE":
                    for (int i = 1; i < parts.Length; i++) h.Types.Add(char.ToUpperInvariant(parts[i][0]));
                    sawTypes = true;
                    break;
                case "COUNT":
                    for (int i = 1; i < parts.Length; i++) h.Counts.Add(int.Parse(parts[i], CultureInfo.InvariantCulture));
                    break;
                case "WIDTH":
                    h.Width = int.Parse(parts[1], CultureInfo.InvariantCulture);
                    break;
                case "HEIGHT":
                    h.Height = int.Parse(parts[1], CultureInfo.InvariantCulture);
                    break;
                case "POINTS":
                    h.Points = int.Parse(parts[1], CultureInfo.InvariantCulture);
                    break;
            }
            pos = next;
        }

        if (!sawFields || !sawSizes || !sawTypes)
            throw new InvalidDataException($"{label ?? "<pcd>"}: incomplete PCD header (need FIELDS/SIZE/TYPE).");
        if (h.Counts.Count == 0)
            for (int i = 0; i < h.Names.Count; i++) h.Counts.Add(1);
        if (h.Names.Count != h.Sizes.Count || h.Names.Count != h.Types.Count || h.Names.Count != h.Counts.Count)
            throw new InvalidDataException($"{label ?? "<pcd>"}: FIELDS/SIZE/TYPE/COUNT length mismatch.");

        if (h.Points <= 0)
            h.Points = h.Width * h.Height;

        int offset = 0;
        for (int i = 0; i < h.Names.Count; i++)
        {
            int size = h.Sizes[i];
            int count = h.Counts[i];
            PointFieldDataType type = MapPcdType(h.Types[i], size);
            h.Fields.Add(new PointField(h.Names[i], offset, type, count));
            offset += size * count;
        }
        h.PointStep = offset;

        if (h.Format.Length == 0)
            throw new InvalidDataException($"{label ?? "<pcd>"}: missing DATA line in PCD header.");
        return h;
    }

    private static PointFieldDataType MapPcdType(char type, int size) => (type, size) switch
    {
        ('F', 4) => PointFieldDataType.Float32,
        ('F', 8) => PointFieldDataType.Float64,
        ('I', 1) => PointFieldDataType.Int8,
        ('I', 2) => PointFieldDataType.Int16,
        ('I', 4) => PointFieldDataType.Int32,
        ('U', 1) => PointFieldDataType.UInt8,
        ('U', 2) => PointFieldDataType.UInt16,
        ('U', 4) => PointFieldDataType.UInt32,
        _ => throw new InvalidDataException($"Unsupported PCD field type '{type}' size {size}."),
    };


    private static void FillPcdAscii(string text, PcdHeader header, byte[] data, string? label)
    {
        using var reader = new StringReader(text.Substring(header.DataStart));
        string? line;
        int pointIndex = 0;
        while (pointIndex < header.Points && (line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
                continue;

            int token = 0;
            foreach (PointField field in header.Fields)
            {
                int elementSize = PointField.GetElementSize(field.DataType);
                for (int e = 0; e < field.Count; e++)
                {
                    if (token >= tokens.Length)
                        throw new InvalidDataException($"{label ?? "<pcd>"}: too few tokens in ascii point {pointIndex}.");
                    int off = pointIndex * header.PointStep + field.Offset + e * elementSize;
                    WriteToken(data, off, field.DataType, tokens[token++]);
                }
            }
            pointIndex++;
        }

        if (pointIndex != header.Points)
            throw new InvalidDataException($"{label ?? "<pcd>"}: expected {header.Points} points but parsed {pointIndex}.");
    }

    private static void WriteToken(byte[] buf, int offset, PointFieldDataType type, string token)
    {
        Span<byte> span = buf.AsSpan(offset, PointField.GetElementSize(type));
        switch (type)
        {
            case PointFieldDataType.Float32:
                BinaryPrimitives.WriteSingleLittleEndian(span, float.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture));
                break;
            case PointFieldDataType.Float64:
                BinaryPrimitives.WriteDoubleLittleEndian(span, double.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture));
                break;
            case PointFieldDataType.Int8:
                span[0] = unchecked((byte)(sbyte)int.Parse(token, NumberStyles.Integer, CultureInfo.InvariantCulture));
                break;
            case PointFieldDataType.UInt8:
                span[0] = byte.Parse(token, NumberStyles.Integer, CultureInfo.InvariantCulture);
                break;
            case PointFieldDataType.Int16:
                BinaryPrimitives.WriteInt16LittleEndian(span, short.Parse(token, NumberStyles.Integer, CultureInfo.InvariantCulture));
                break;
            case PointFieldDataType.UInt16:
                BinaryPrimitives.WriteUInt16LittleEndian(span, ushort.Parse(token, NumberStyles.Integer, CultureInfo.InvariantCulture));
                break;
            case PointFieldDataType.Int32:
                BinaryPrimitives.WriteInt32LittleEndian(span, int.Parse(token, NumberStyles.Integer, CultureInfo.InvariantCulture));
                break;
            case PointFieldDataType.UInt32:
                BinaryPrimitives.WriteUInt32LittleEndian(span, uint.Parse(token, NumberStyles.Integer, CultureInfo.InvariantCulture));
                break;
        }
    }


    /// <summary>Reads a Stanford PLY file (ascii or binary_little_endian).</summary>
    public static PointCloud2Data ReadPly(string path, string? frameId = null)
    {
        if (path is null)
            throw new ArgumentNullException(nameof(path));
        return ParsePly(File.ReadAllBytes(path), frameId, path);
    }

    /// <summary>Parses a PLY file from its raw bytes.</summary>
    public static PointCloud2Data ParsePly(byte[] bytes, string? frameId = null, string? label = null)
    {
        if (bytes is null)
            throw new ArgumentNullException(nameof(bytes));

        string text = Encoding.ASCII.GetString(bytes);
        string format;
        List<PlyElement> elements = ParsePlyHeader(text, out int dataStart, out format);

        PlyElement? vertex = elements.Find(e => e.Name is "vertex" or "vertices");
        if (vertex is null)
            throw new InvalidDataException($"{label ?? "<ply>"}: no 'vertex' element found.");

        List<PointField> fields = BuildVertexFields(vertex);
        int pointStep = 0;
        foreach (PointField f in fields) pointStep += f.Size;

        byte[] data;
        switch (format)
        {
            case "ascii":
                data = FillPlyAscii(text, dataStart, elements, vertex, fields, pointStep, label);
                break;
            case "binary_little_endian":
                data = FillPlyBinary(bytes, dataStart, elements, vertex, pointStep, label);
                break;
            case "binary_big_endian":
                throw new NotSupportedException($"{label ?? "<ply>"}: binary_big_endian PLY is not supported.");
            default:
                throw new InvalidDataException($"{label ?? "<ply>"}: unknown PLY format '{format}'.");
        }

        return new PointCloud2Data(fields.ToArray(), data, pointStep,
            frameId, width: vertex.Count, height: 1);
    }


    private sealed class PlyProperty
    {
        public string Name = "";
        public PointFieldDataType Type;
        public bool IsList;
        public PointFieldDataType CountType;
        public PointFieldDataType ItemType;
        public int GetElementSize() => PointField.GetElementSize(IsList ? ItemType : Type);
    }

    private sealed class PlyElement
    {
        public string Name = "";
        public int Count;
        public List<PlyProperty> Properties = new();
    }

    private static List<PlyElement> ParsePlyHeader(string text, out int dataStart, out string format)
    {
        var elements = new List<PlyElement>();
        format = "ascii";
        dataStart = 0;
        int pos = 0;
        while (pos < text.Length)
        {
            int nl = text.IndexOf('\n', pos);
            string line = text[pos..(nl < 0 ? text.Length : nl)].TrimEnd('\r');
            int next = nl < 0 ? text.Length : nl + 1;

            if (line.Length == 0 || line.StartsWith("comment") || line.StartsWith("obj_info") || line.StartsWith("ply"))
            {
                pos = next;
                continue;
            }
            if (line.StartsWith("end_header"))
            {
                dataStart = next;
                break;
            }

            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                pos = next;
                continue;
            }

            switch (parts[0])
            {
                case "format":
                    format = parts[1];
                    break;
                case "element":
                    elements.Add(new PlyElement
                    {
                        Name = parts[1],
                        Count = int.Parse(parts[2], CultureInfo.InvariantCulture),
                    });
                    break;
                case "property":
                    PlyElement current = elements[^1];
                    if (parts[1] == "list")
                    {
                        current.Properties.Add(new PlyProperty
                        {
                            Name = parts[4],
                            IsList = true,
                            CountType = MapPlyType(parts[2]),
                            ItemType = MapPlyType(parts[3]),
                        });
                    }
                    else
                    {
                        current.Properties.Add(new PlyProperty
                        {
                            Name = parts[2],
                            Type = MapPlyType(parts[1]),
                        });
                    }
                    break;
            }
            pos = next;
        }
        return elements;
    }

    private static PointFieldDataType MapPlyType(string type) => type switch
    {
        "char" or "int8" => PointFieldDataType.Int8,
        "uchar" or "uint8" => PointFieldDataType.UInt8,
        "short" or "int16" => PointFieldDataType.Int16,
        "ushort" or "uint16" => PointFieldDataType.UInt16,
        "int" or "int32" => PointFieldDataType.Int32,
        "uint" or "uint32" => PointFieldDataType.UInt32,
        "float" or "float32" => PointFieldDataType.Float32,
        "double" or "float64" => PointFieldDataType.Float64,
        _ => throw new InvalidDataException($"Unsupported PLY property type '{type}'."),
    };

    private static List<PointField> BuildVertexFields(PlyElement vertex)
    {
        var fields = new List<PointField>();
        int offset = 0;
        foreach (PlyProperty p in vertex.Properties)
        {
            if (p.IsList)
                throw new NotSupportedException("PLY vertex list properties are not supported.");
            fields.Add(new PointField(p.Name, offset, p.Type, 1));
            offset += p.GetElementSize();
        }
        return fields;
    }

    /// <summary>Reads whitespace-separated ASCII tokens spanning multiple lines.</summary>
    private sealed class PlyTokenReader
    {
        private readonly TextReader _reader;

        public PlyTokenReader(TextReader reader) => _reader = reader;

        public string? Next()
        {
            int c;
            while ((c = _reader.Read()) != -1)
            {
                if (c != ' ' && c != '\t' && c != '\r' && c != '\n')
                    break;
            }
            if (c == -1)
                return null;

            var sb = new StringBuilder();
            sb.Append((char)c);
            while ((c = _reader.Read()) != -1)
            {
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n')
                    break;
                sb.Append((char)c);
            }
            return sb.ToString();
        }
    }


    private static byte[] FillPlyAscii(string text, int dataStart, List<PlyElement> elements,
        PlyElement vertex, List<PointField> fields, int pointStep, string? label)
    {
        var data = new byte[vertex.Count * pointStep];
        using var reader = new StringReader(text.Substring(dataStart));
        var tokens = new PlyTokenReader(reader);

        foreach (PlyElement element in elements)
        {
            for (int i = 0; i < element.Count; i++)
            {
                if (element == vertex)
                    FillPlyVertexAscii(tokens, data, i, fields, pointStep, label);
                else
                    SkipPlyElementAscii(tokens, element, label);
            }
        }
        return data;
    }

    private static void FillPlyVertexAscii(PlyTokenReader tokens, byte[] data, int point,
        List<PointField> fields, int pointStep, string? label)
    {
        foreach (PointField field in fields)
        {
            string? token = tokens.Next();
            if (token is null)
                throw new InvalidDataException($"{label ?? "<ply>"}: unexpected end of data at vertex {point}.");
            int off = point * pointStep + field.Offset;
            WriteToken(data, off, field.DataType, token);
        }
    }

    private static void SkipPlyElementAscii(PlyTokenReader tokens, PlyElement element, string? label)
    {
        foreach (PlyProperty p in element.Properties)
        {
            if (p.IsList)
            {
                string? countToken = tokens.Next();
                if (countToken is null)
                    throw new InvalidDataException($"{label ?? "<ply>"}: unexpected end of data.");
                int count = int.Parse(countToken, CultureInfo.InvariantCulture);
                for (int i = 0; i < count; i++)
                {
                    if (tokens.Next() is null)
                        throw new InvalidDataException($"{label ?? "<ply>"}: unexpected end of data in list property.");
                }
            }
            else
            {
                if (tokens.Next() is null)
                    throw new InvalidDataException($"{label ?? "<ply>"}: unexpected end of data.");
            }
        }
    }

    private static byte[] FillPlyBinary(byte[] bytes, int dataStart, List<PlyElement> elements,
        PlyElement vertex, int pointStep, string? label)
    {
        var data = new byte[vertex.Count * pointStep];
        int cursor = dataStart;

        foreach (PlyElement element in elements)
        {
            if (element == vertex)
            {
                int need = element.Count * pointStep;
                if (cursor + need > bytes.Length)
                    throw new InvalidDataException($"{label ?? "<ply>"}: vertex payload exceeds file length.");
                Array.Copy(bytes, cursor, data, 0, need);
                cursor += need;
            }
            else
            {
                for (int i = 0; i < element.Count; i++)
                    cursor = SkipPlyElementBinary(bytes, cursor, element, label);
            }
        }
        return data;
    }

    private static int SkipPlyElementBinary(byte[] bytes, int cursor, PlyElement element, string? label)
    {
        foreach (PlyProperty p in element.Properties)
        {
            if (p.IsList)
            {
                int count = ReadPlyScalar(bytes, ref cursor, p.CountType, label);
                cursor += count * PointField.GetElementSize(p.ItemType);
            }
            else
            {
                cursor += PointField.GetElementSize(p.Type);
            }
        }
        return cursor;
    }

    private static int ReadPlyScalar(byte[] bytes, ref int cursor, PointFieldDataType type, string? label)
    {
        int size = PointField.GetElementSize(type);
        if (cursor + size > bytes.Length)
            throw new InvalidDataException($"{label ?? "<ply>"}: unexpected end of binary data.");
        ReadOnlySpan<byte> span = bytes.AsSpan(cursor, size);
        cursor += size;
        return type switch
        {
            PointFieldDataType.Int8 => (sbyte)span[0],
            PointFieldDataType.UInt8 => span[0],
            PointFieldDataType.Int16 => BinaryPrimitives.ReadInt16LittleEndian(span),
            PointFieldDataType.UInt16 => BinaryPrimitives.ReadUInt16LittleEndian(span),
            PointFieldDataType.Int32 => BinaryPrimitives.ReadInt32LittleEndian(span),
            PointFieldDataType.UInt32 => (int)BinaryPrimitives.ReadUInt32LittleEndian(span),
            PointFieldDataType.Float32 => (int)BinaryPrimitives.ReadSingleLittleEndian(span),
            PointFieldDataType.Float64 => (int)BinaryPrimitives.ReadDoubleLittleEndian(span),
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
        };
    }
}
