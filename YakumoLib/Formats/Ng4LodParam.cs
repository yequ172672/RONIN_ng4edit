using System.Buffers.Binary;
using System.Text;

namespace YakumoLib.Formats;

public sealed record Ng4LodParamEntry(string Name, uint Index, float ScreenCoverage);

public static class Ng4LodParam
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static IReadOnlyList<Ng4LodParamEntry> Parse(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        return ParseLayoutsChecked(data).Select(layout => layout.Entry).ToArray();
    }

    public static byte[] DisableChildLods(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        LodLayout[] layouts = ParseLayoutsChecked(data);
        byte[] output = (byte[])data.Clone();
        foreach (LodLayout layout in layouts)
        {
            if (layout.Entry.Index == 0 || !IsLodName(layout.Entry.Name)) continue;
            BinaryPrimitives.WriteSingleLittleEndian(output.AsSpan(layout.ScreenCoverageOffset, 4), 0f);
        }
        return output;
    }

    private static LodLayout[] ParseLayoutsChecked(byte[] data)
    {
        try { return ParseLayouts(data); }
        catch (OverflowException error) { throw new InvalidDataException("LodParam contains overflowing bounds or counts.", error); }
    }

    private static LodLayout[] ParseLayouts(byte[] data)
    {
        const int rootStart = 4;
        RequireRange(data.Length, rootStart, 8, "root object");
        ObjectLayout root = ReadObject(data, rootStart, data.Length, "root object");
        if (root.End != data.Length)
            throw new InvalidDataException("LodParam root object does not cover the complete file.");
        int listStart = RequireField(root, 0, "LOD record list");
        RequireRange(root.End, listStart, 8, "LOD record list");
        uint recordCount = ReadUInt32(data, listStart);
        uint listLength = ReadUInt32(data, listStart + 4);
        int listEnd = CheckedEnd(listStart, listLength, root.End, "LOD record list");
        int listHeaderSize = checked(8 + checked((int)recordCount * 4));
        RequireRange(listEnd, listStart, listHeaderSize, "LOD record offsets");

        var layouts = new LodLayout[checked((int)recordCount)];
        int previousObjectStart = -1;
        for (int index = 0; index < layouts.Length; index++)
        {
            uint relativeOffset = ReadUInt32(data, checked(listStart + 8 + index * 4));
            int objectStart = checked(listStart + checked((int)relativeOffset));
            if (relativeOffset < listHeaderSize || objectStart >= listEnd || objectStart <= previousObjectStart)
                throw new InvalidDataException($"LodParam record {index} has an invalid offset.");
            previousObjectStart = objectStart;

            ObjectLayout record = ReadObject(data, objectStart, listEnd, $"LOD record {index}");
            int nameField = RequireField(record, 0, $"LOD record {index} name");
            int indexField = RequireField(record, 1, $"LOD record {index} index");
            int coverageField = RequireField(record, 2, $"LOD record {index} screen coverage");
            string name = ReadString(data, nameField, record.End, $"LOD record {index} name");
            RequireRange(record.End, indexField, 4, $"LOD record {index} index");
            RequireRange(record.End, coverageField, 4, $"LOD record {index} screen coverage");
            uint lodIndex = ReadUInt32(data, indexField);
            float coverage = BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(coverageField, 4));
            if (!float.IsFinite(coverage))
                throw new InvalidDataException($"LOD record {index} screen coverage is not finite.");
            layouts[index] = new LodLayout(new Ng4LodParamEntry(name, lodIndex, coverage), coverageField);
        }
        return layouts;
    }

    private static ObjectLayout ReadObject(byte[] data, int start, int containingEnd, string label)
    {
        RequireRange(containingEnd, start, 8, label);
        uint objectLength = ReadUInt32(data, start);
        int end = CheckedEnd(start, objectLength, containingEnd, label);
        uint fieldCount = ReadUInt32(data, start + 4);
        int tableSize = checked(8 + checked((int)fieldCount * 8));
        RequireRange(end, start, tableSize, label + " field table");
        var fields = new Dictionary<uint, int>(checked((int)fieldCount));
        for (int index = 0; index < fieldCount; index++)
        {
            int pairOffset = checked(start + 8 + index * 8);
            uint fieldId = ReadUInt32(data, pairOffset);
            uint relativeOffset = ReadUInt32(data, pairOffset + 4);
            if (relativeOffset >= objectLength)
                throw new InvalidDataException($"{label} field {fieldId} points outside the object.");
            int fieldOffset = checked(start + checked((int)relativeOffset));
            if (!fields.TryAdd(fieldId, fieldOffset))
                throw new InvalidDataException($"{label} contains duplicate field {fieldId}.");
        }
        return new ObjectLayout(end, fields);
    }

    private static string ReadString(byte[] data, int offset, int containingEnd, string label)
    {
        RequireRange(containingEnd, offset, 4, label);
        uint byteLength = ReadUInt32(data, offset);
        if (byteLength == 0)
            throw new InvalidDataException($"{label} is empty.");
        int bytesStart = checked(offset + 4);
        RequireRange(containingEnd, bytesStart, checked((int)byteLength), label);
        ReadOnlySpan<byte> bytes = data.AsSpan(bytesStart, checked((int)byteLength));
        if (bytes[^1] != 0 || bytes[..^1].Contains((byte)0))
            throw new InvalidDataException($"{label} is not one null-terminated string.");
        try { return StrictUtf8.GetString(bytes[..^1]); }
        catch (DecoderFallbackException error) { throw new InvalidDataException($"{label} is not valid UTF-8.", error); }
    }

    private static int RequireField(ObjectLayout layout, uint fieldId, string label) =>
        layout.Fields.TryGetValue(fieldId, out int offset)
            ? offset
            : throw new InvalidDataException($"LodParam is missing {label} field {fieldId}.");

    private static uint ReadUInt32(byte[] data, int offset) =>
        BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset, 4));

    private static int CheckedEnd(int start, uint length, int containingEnd, string label)
    {
        if (length > int.MaxValue)
            throw new InvalidDataException($"{label} is too large.");
        int end;
        try { end = checked(start + (int)length); }
        catch (OverflowException error) { throw new InvalidDataException($"{label} bounds overflowed.", error); }
        if (length < 8 || end > containingEnd)
            throw new InvalidDataException($"{label} exceeds its containing data.");
        return end;
    }

    private static void RequireRange(int containingEnd, int offset, int size, string label)
    {
        if (offset < 0 || size < 0 || offset > containingEnd || size > containingEnd - offset)
            throw new InvalidDataException($"{label} is outside its containing data.");
    }

    private static bool IsLodName(string name)
    {
        if (name.Length <= 3 || !name.StartsWith("LOD", StringComparison.OrdinalIgnoreCase)) return false;
        for (int index = 3; index < name.Length; index++)
            if (!char.IsAsciiDigit(name[index])) return false;
        return true;
    }

    private sealed record ObjectLayout(int End, IReadOnlyDictionary<uint, int> Fields);
    private sealed record LodLayout(Ng4LodParamEntry Entry, int ScreenCoverageOffset);
}
