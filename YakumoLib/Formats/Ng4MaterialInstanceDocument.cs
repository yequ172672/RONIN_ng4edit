using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace YakumoLib.Formats;

public enum Ng4MaterialParameterType
{
    TextureUuid,
    Float,
    Vector4,
    Opaque
}

public sealed record Ng4MaterialSection(int Id, int Offset, int Length, ReadOnlyMemory<byte> RawBytes);

public sealed record Ng4MaterialRecord(
    int SectionId,
    int Offset,
    int Length,
    ReadOnlyMemory<byte> RawBytes,
    Ng4MaterialParameter? Parameter);

public sealed record Ng4MaterialParameter(
    string Name,
    Ng4MaterialParameterType Type,
    int SectionId,
    int ValueOffset,
    int ValueLength,
    object? Value,
    bool IsTyped,
    ReadOnlyMemory<byte> RawRecord);

public sealed class Ng4MaterialInstanceDocument
{
    private const int SectionCount = 7;
    private const int HeaderLength = 0x44;

    private Ng4MaterialInstanceDocument(
        UUID? parentAssetId,
        IReadOnlyList<Ng4MaterialSection> sections,
        IReadOnlyList<Ng4MaterialParameter> parameters,
        IReadOnlyList<Ng4MaterialRecord> records)
    {
        ParentAssetId = parentAssetId;
        Sections = sections;
        Parameters = parameters;
        Records = records;
    }

    public UUID? ParentAssetId { get; }
    public IReadOnlyList<Ng4MaterialSection> Sections { get; }
    public IReadOnlyList<Ng4MaterialParameter> Parameters { get; }
    public IReadOnlyList<Ng4MaterialRecord> Records { get; }

    public static Ng4MaterialInstanceDocument Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderLength + 16)
            throw new InvalidDataException("Instance.dat header or parent UUID is truncated.");
        int declaredLength;
        try
        {
            declaredLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(4, 4)) + 4);
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException("Instance.dat declared length is invalid.", exception);
        }
        if (declaredLength != data.Length || BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(8, 4)) != SectionCount)
            throw new InvalidDataException("Instance.dat header is invalid.");

        var offsets = new int[SectionCount];
        for (int id = 0; id < SectionCount; id++)
        {
            int entryOffset = 12 + id * 8;
            if (BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(entryOffset, 4)) != id)
                throw new InvalidDataException("Instance.dat section identifiers are invalid.");
            try
            {
                offsets[id] = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(entryOffset + 4, 4)) + 4);
            }
            catch (OverflowException exception)
            {
                throw new InvalidDataException($"Instance.dat section {id} offset is invalid.", exception);
            }
            if (offsets[id] < HeaderLength || offsets[id] > data.Length || (id > 0 && offsets[id] < offsets[id - 1]))
                throw new InvalidDataException($"Instance.dat section {id} offset is invalid.");
        }
        if (offsets[0] > data.Length - 16)
            throw new InvalidDataException("Instance.dat parent UUID is out of bounds.");

        byte[] owned = data.ToArray();
        var sections = new Ng4MaterialSection[SectionCount];
        for (int id = 0; id < SectionCount; id++)
        {
            int end = id + 1 < SectionCount ? offsets[id + 1] : data.Length;
            int length = checked(end - offsets[id]);
            sections[id] = new Ng4MaterialSection(id, offsets[id], length, owned.AsSpan(offsets[id], length).ToArray());
        }
        if (sections[0].Length < 16)
            throw new InvalidDataException("Instance.dat parent UUID crosses the section 0 boundary.");

        UUID parentValue = ReadUuid(owned, offsets[0]);
        UUID? parent = IsZero(parentValue) ? null : parentValue;
        var parameters = new List<Ng4MaterialParameter>();
        var records = new List<Ng4MaterialRecord>();
        for (int id = 1; id < SectionCount; id++)
        {
            bool exposeParameters = id is 1 or 2 or 3 or 4 or 5;
            (IReadOnlyList<Ng4MaterialParameter> sectionParameters, IReadOnlyList<Ng4MaterialRecord> sectionRecords) =
                ParseRecords(owned, sections[id], exposeParameters, strict: id == 3);
            parameters.AddRange(sectionParameters);
            records.AddRange(sectionRecords);
        }
        return new Ng4MaterialInstanceDocument(
            parent,
            Array.AsReadOnly(sections),
            Array.AsReadOnly(parameters.ToArray()),
            Array.AsReadOnly(records.OrderBy(record => record.Offset).ToArray()));
    }

    private static (IReadOnlyList<Ng4MaterialParameter> Parameters, IReadOnlyList<Ng4MaterialRecord> Records)
        ParseRecords(byte[] owned, Ng4MaterialSection section, bool exposeParameters, bool strict)
    {
        if (section.Length == 0)
            return (Array.Empty<Ng4MaterialParameter>(), Array.Empty<Ng4MaterialRecord>());
        TextureContainer container;
        try
        {
            container = ReadTextureContainer(owned, section);
        }
        catch (InvalidDataException) when (!strict)
        {
            if (!TryReadIndexedContainer(owned, section, out container))
                return (Array.Empty<Ng4MaterialParameter>(), Array.Empty<Ng4MaterialRecord>());
        }
        var parameters = new List<Ng4MaterialParameter>(container.Records.Count);
        var records = new List<Ng4MaterialRecord>(container.Records.Count);
        foreach (TextureRecord record in container.Records)
        {
            int absoluteRecordOffset = checked(section.Offset + record.Offset);
            ReadOnlySpan<byte> rawRecord = owned.AsSpan(absoluteRecordOffset, record.Length);
            Ng4MaterialParameter? parameter = null;
            bool named;
            string? name = null;
            int relativeValueOffset = 0;
            int valueLength = 0;
            try
            {
                named = TryReadNamedRecord(rawRecord, out name, out relativeValueOffset, out valueLength);
            }
            catch (InvalidDataException) when (!strict)
            {
                named = false;
            }
            if (named)
            {
                int valueOffset = checked(absoluteRecordOffset + relativeValueOffset);
                Ng4MaterialParameterType type = exposeParameters
                    ? ClassifyParameter(section.Id, rawRecord, owned.AsSpan(valueOffset, valueLength))
                    : Ng4MaterialParameterType.Opaque;
                object value = ReadValue(owned, valueOffset, valueLength, type);
                parameter = new Ng4MaterialParameter(
                    name!,
                    type,
                    section.Id,
                    valueOffset,
                    valueLength,
                    value,
                    type != Ng4MaterialParameterType.Opaque,
                    owned.AsSpan(absoluteRecordOffset, record.Length).ToArray());
                if (exposeParameters)
                    parameters.Add(parameter);
            }
            records.Add(new Ng4MaterialRecord(
                section.Id,
                absoluteRecordOffset,
                record.Length,
                owned.AsSpan(absoluteRecordOffset, record.Length).ToArray(),
                parameter));
        }
        return (Array.AsReadOnly(parameters.ToArray()), Array.AsReadOnly(records.ToArray()));
    }

    private static bool TryReadIndexedContainer(
        byte[] data,
        Ng4MaterialSection section,
        out TextureContainer container)
    {
        container = new TextureContainer(0, Array.Empty<TextureRecord>());
        if (section.Length < 8) return false;
        ReadOnlySpan<byte> bytes = data.AsSpan(section.Offset, section.Length);
        int length;
        int count;
        try
        {
            length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes));
            count = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(4)));
            int tableEnd = checked(8 + count * 8);
            if (length < tableEnd || length > section.Length || count > (length - 8) / 8)
                return false;

            var offsets = new int[count];
            int previous = tableEnd;
            for (int index = 0; index < count; index++)
            {
                int entry = checked(8 + index * 8);
                if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(entry, 4)) != index)
                    return false;
                int offset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(entry + 4, 4)));
                if (offset < previous || offset > length) return false;
                offsets[index] = offset;
                previous = offset;
            }

            var records = new TextureRecord[count];
            for (int index = 0; index < count; index++)
            {
                int end = index + 1 < count ? offsets[index + 1] : length;
                records[index] = new TextureRecord(offsets[index], checked(end - offsets[index]));
            }
            container = new TextureContainer(length, Array.AsReadOnly(records));
            return true;
        }
        catch (OverflowException)
        {
            return false;
        }
    }

    private static TextureContainer ReadTextureContainer(byte[] data, Ng4MaterialSection section)
    {
        if (section.Length < 8)
            throw new InvalidDataException("Instance.dat texture section is truncated.");
        ReadOnlySpan<byte> bytes = data.AsSpan(section.Offset, section.Length);
        int count;
        int length;
        try
        {
            count = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes));
            length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(4)));
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException("Instance.dat texture container header is invalid.", exception);
        }
        if (length < 8 || length > section.Length || count > (length - 8) / 4)
            throw new InvalidDataException("Instance.dat texture container header is invalid.");

        int tableEnd = checked(8 + count * 4);
        var records = new List<TextureRecord>(count);
        int previousEnd = tableEnd;
        for (int index = 0; index < count; index++)
        {
            int offset;
            int recordLength;
            try
            {
                offset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(8 + index * 4, 4)));
                if (offset < previousEnd || offset > length - 4)
                    throw new InvalidDataException($"Instance.dat texture record {index} offset is invalid.");
                recordLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, 4)));
            }
            catch (OverflowException exception)
            {
                throw new InvalidDataException($"Instance.dat texture record {index} bounds are invalid.", exception);
            }
            if (recordLength < 4 || recordLength > length - offset)
                throw new InvalidDataException($"Instance.dat texture record {index} length is invalid.");
            previousEnd = checked(offset + recordLength);
            records.Add(new TextureRecord(offset, recordLength));
        }
        return new TextureContainer(length, records);
    }

    private static bool TryReadNamedRecord(
        ReadOnlySpan<byte> record,
        out string? name,
        out int valueOffset,
        out int valueLength)
    {
        name = null;
        valueOffset = 0;
        valueLength = 0;
        if (record.Length < 28 || BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(12, 4)) != 24)
            return false;

        int nameLength;
        try
        {
            nameLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(24, 4)));
            valueOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(20, 4)));
        }
        catch (OverflowException exception)
        {
            throw new InvalidDataException("Instance.dat named record metadata is invalid.", exception);
        }
        if (nameLength <= 1 || nameLength > record.Length - 28 ||
            valueOffset < 28 + nameLength || valueOffset > record.Length)
            throw new InvalidDataException("Instance.dat named record bounds are invalid.");
        ReadOnlySpan<byte> rawName = record.Slice(28, nameLength);
        if (rawName[^1] != 0 || rawName[..^1].Contains((byte)0))
            throw new InvalidDataException("Instance.dat named record name is not valid null-terminated UTF-8.");
        try
        {
            name = new UTF8Encoding(false, true).GetString(rawName[..^1]);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("Instance.dat named record name is not valid UTF-8.", exception);
        }
        valueLength = checked(record.Length - valueOffset);
        return true;
    }

    private static Ng4MaterialParameterType ClassifyParameter(
        int sectionId,
        ReadOnlySpan<byte> record,
        ReadOnlySpan<byte> value)
    {
        if (record.Length < 32 ||
            BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(4, 4)) != 2 ||
            BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(8, 4)) != 0 ||
            BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(12, 4)) != 24 ||
            BinaryPrimitives.ReadUInt32LittleEndian(record.Slice(16, 4)) != 1)
            return Ng4MaterialParameterType.Opaque;
        if (sectionId == 1 && value.Length == 4)
            return float.IsFinite(ReadSingle(value)) ? Ng4MaterialParameterType.Float : Ng4MaterialParameterType.Opaque;
        if (sectionId == 2 && value.Length == 16)
        {
            Vector4 vector = ReadVector4(value);
            return IsFinite(vector) ? Ng4MaterialParameterType.Vector4 : Ng4MaterialParameterType.Opaque;
        }
        if (sectionId == 3 && value.Length == 16)
            return Ng4MaterialParameterType.TextureUuid;
        return Ng4MaterialParameterType.Opaque;
    }

    private static object ReadValue(byte[] data, int offset, int length, Ng4MaterialParameterType type) => type switch
    {
        Ng4MaterialParameterType.TextureUuid => ReadUuid(data, offset),
        Ng4MaterialParameterType.Float => ReadSingle(data.AsSpan(offset, 4)),
        Ng4MaterialParameterType.Vector4 => ReadVector4(data.AsSpan(offset, 16)),
        _ => new ReadOnlyMemory<byte>(data.AsSpan(offset, length).ToArray())
    };

    private static float ReadSingle(ReadOnlySpan<byte> data) =>
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data));

    private static Vector4 ReadVector4(ReadOnlySpan<byte> data) => new(
        ReadSingle(data), ReadSingle(data.Slice(4)), ReadSingle(data.Slice(8)), ReadSingle(data.Slice(12)));

    private static bool IsFinite(Vector4 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) && float.IsFinite(value.W);

    private static UUID ReadUuid(ReadOnlySpan<byte> data, int offset) => new()
    {
        a = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)),
        b = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 4, 4)),
        c = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 8, 4)),
        d = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 12, 4))
    };

    private static bool IsZero(UUID value) => value.a == 0 && value.b == 0 && value.c == 0 && value.d == 0;

    private sealed record TextureContainer(int Length, IReadOnlyList<TextureRecord> Records);
    private sealed record TextureRecord(int Offset, int Length);
}
