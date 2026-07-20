using YakumoLib;
using YakumoLib.Assets;
using YakumoLib.Modding;

internal static class ParentPayloadBuilderTests
{
    public static void Run()
    {
        LocalSubfileReplacementPreservesOrderAndCopiesUnchangedBytes();
        WholeParentReplacementReturnsSuppliedBytes();
        WholeParentReplacementRejectsOutOfRangeTemplateSubentry();
        MixingWholeParentAndSubfileReplacementThrows();
        MultipleWholeParentReplacementsThrow();
        SubentryNotBelongingToParentThrows();
        GlobalSubfileReplacementPreservesGlobalAddressing();
        WholeParentReplacementNormalizesGlobalAddressing();
        SameIdentityWithDifferentStoragePathThrows();
        SameIdentityWithDifferentSourceArchiveThrows();
        SameIdentityWithDifferentSubentryLayoutThrows();
        SameNameForeignSubentryMetadataMismatchThrows();
        DuplicateSubentryReferenceThrows();
        DuplicateSubentryNameIgnoringCaseThrows();
        PayloadOwnsReadOnlyDataAndSubentrySnapshots();
        Console.WriteLine("Parent payload builder focused tests passed.");
    }

    private static void LocalSubfileReplacementPreservesOrderAndCopiesUnchangedBytes()
    {
        string directory = CreateTempDirectory();
        try
        {
            byte[] sourceBytes = [1, 2, 3, 4, 5, 6];
            File.WriteAllBytes(Path.Combine(directory, "synthetic.dat"), sourceBytes);
            AssetEntry parent = CreateParent(directory,
            [
                CreateSub(directory, "first.bin", 0, 2),
                CreateSub(directory, "second.bin", 2, 3),
                CreateSub(directory, "third.bin", 5, 1)
            ], sourceBytes.LongLength);
            SubAssetEntry replacementTarget = parent.SubEntries![1];
            byte[] replacementBytes = [9, 8, 7, 6];

            ParentPayload payload = ParentPayloadBuilder.BuildAll(
            [
                new ModifiedAssetEntry
                {
                    ParentEntry = parent,
                    SubEntry = replacementTarget,
                    ModifiedData = replacementBytes,
                    OriginalData = [3, 4, 5],
                    Compress = false
                }
            ]).Single();

            AssertSequenceEqual([1, 2, 9, 8, 7, 6, 6], payload.Data, "rebuilt parent bytes");
            AssertSequenceEqual(sourceBytes, File.ReadAllBytes(Path.Combine(directory, "synthetic.dat")), "source parent mutation");
            AssertEqual(3, payload.SubEntries.Count, "subentry count");
            AssertSubEntry(payload.SubEntries[0], "first.bin", 0L, 2L, false);
            AssertSubEntry(payload.SubEntries[1], "second.bin", 2L, 4L, false);
            AssertSubEntry(payload.SubEntries[2], "third.bin", 6L, 1L, false);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void WholeParentReplacementReturnsSuppliedBytes()
    {
        AssetEntry parent = CreateParent("unused",
        [
            CreateSub("unused", "first.bin", 1, 2),
            CreateSub("unused", "second.bin", 5, 3)
        ], 8);
        byte[] replacement = [10, 11, 12, 13, 14, 15, 16, 17];

        ParentPayload payload = ParentPayloadBuilder.BuildAll([CreateWholeParentChange(parent, replacement)]).Single();

        replacement[0] = 99;
        AssertSequenceEqual([10, 11, 12, 13, 14, 15, 16, 17], payload.Data, "whole parent bytes");
        AssertSubEntry(payload.SubEntries[0], "first.bin", 1L, 2L, false);
        AssertSubEntry(payload.SubEntries[1], "second.bin", 5L, 3L, false);
    }

    private static void WholeParentReplacementRejectsOutOfRangeTemplateSubentry()
    {
        AssetEntry parent = CreateParent("unused", [CreateSub("unused", "outside.bin", 4, 2)], 6);
        AssertThrows<InvalidDataException>(() =>
            ParentPayloadBuilder.BuildAll([CreateWholeParentChange(parent, [1, 2, 3, 4, 5])]),
            "whole-parent template bounds");
    }

    private static void MixingWholeParentAndSubfileReplacementThrows()
    {
        AssetEntry parent = CreateParent("unused", [CreateSub("unused", "part.bin", 0, 1)], 1);
        AssertThrows<InvalidOperationException>(() => ParentPayloadBuilder.BuildAll(
        [
            CreateWholeParentChange(parent, [1]),
            CreateSubfileChange(parent, parent.SubEntries![0], [2])
        ]), "mixed whole-parent and subfile replacements");
    }

    private static void MultipleWholeParentReplacementsThrow()
    {
        AssetEntry parent = CreateParent("unused", [CreateSub("unused", "part.bin", 0, 1)], 1);
        AssertThrows<InvalidOperationException>(() => ParentPayloadBuilder.BuildAll(
        [
            CreateWholeParentChange(parent, [1]),
            CreateWholeParentChange(parent, [2])
        ]), "multiple whole-parent replacements");
    }

    private static void SubentryNotBelongingToParentThrows()
    {
        AssetEntry parent = CreateParent("unused", [CreateSub("unused", "part.bin", 0, 1)], 1);
        SubAssetEntry foreign = CreateSub("unused", "foreign.bin", 0, 1);
        AssertThrows<InvalidOperationException>(() =>
            ParentPayloadBuilder.BuildAll([CreateSubfileChange(parent, foreign, [2])]),
            "foreign subentry");
    }

    private static void GlobalSubfileReplacementPreservesGlobalAddressing()
    {
        string directory = CreateTempDirectory();
        try
        {
            File.WriteAllBytes(Path.Combine(directory, "synthetic.dat"), [1, 2, 3, 4]);
            SubAssetEntry local = CreateSub(directory, "local.bin", 0, 2);
            SubAssetEntry global = CreateSub(directory, "stream.bin", 2, 2, isGlobal: true);
            AssetEntry parent = CreateParent(directory, [local, global], 2);

            ParentPayload payload = ParentPayloadBuilder.BuildAll(
            [
                CreateSubfileChange(parent, global, [9, 8])
            ]).Single();

            AssertSequenceEqual([1, 2, 9, 8], payload.Data, "global rebuilt parent bytes");
            AssertSubEntry(payload.SubEntries[0], "local.bin", 0L, 2L, false);
            AssertSubEntry(payload.SubEntries[1], "stream.bin", 2L, 2L, true);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void WholeParentReplacementNormalizesGlobalAddressing()
    {
        AssetEntry parent = CreateParent("unused",
        [
            CreateSub("unused", "local.bin", 0, 2),
            CreateSub("unused", "stream.bin", 12, 2, isGlobal: true)
        ], 14) with { Offset = 10 };

        ParentPayload payload = ParentPayloadBuilder.BuildAll(
            [CreateWholeParentChange(parent, [1, 2, 3, 4])]).Single();

        AssertSubEntry(payload.SubEntries[0], "local.bin", 0L, 2L, false);
        AssertSubEntry(payload.SubEntries[1], "stream.bin", 2L, 2L, true);
    }

    private static void SameIdentityWithDifferentStoragePathThrows()
    {
        AssertParentIdentityMismatch(parent => parent with
        {
            StoragePath = "Assets/Files/0/0/1/00000001-00000002-00000003-00000004/asset.bin"
        }, "storage path");
    }

    private static void SameIdentityWithDifferentSourceArchiveThrows()
    {
        AssertParentIdentityMismatch(parent => parent with { SourceArchive = "other-archive" }, "source archive");
    }

    private static void SameIdentityWithDifferentSubentryLayoutThrows()
    {
        AssertParentIdentityMismatch(parent => parent with
        {
            SubEntries =
            [
                CreateSub(parent.ContentDirectory!, "first.bin", 0, 2),
                CreateSub(parent.ContentDirectory!, "second.bin", 1, 3)
            ]
        }, "subentry layout");
    }

    private static void SameNameForeignSubentryMetadataMismatchThrows()
    {
        string directory = CreateTempDirectory();
        try
        {
            byte[] sourceBytes = [1, 2];
            File.WriteAllBytes(Path.Combine(directory, "synthetic.dat"), sourceBytes);
            AssetEntry parent = CreateParent(directory, [CreateSub(directory, "part.bin", 0, 2)], sourceBytes.LongLength);
            SubAssetEntry canonical = parent.SubEntries![0];
            var mutations = new (string Label, Func<SubAssetEntry, SubAssetEntry> Mutate)[]
            {
                ("offset", sub => sub with { Offset = 1 }),
                ("size", sub => sub with { Size = 1 }),
                ("compressed size", sub => sub with { CompressedSize = 1 }),
                ("global flag", sub => sub with { IsGlobal = true }),
                ("source archive", sub => sub with { SourceArchive = "other-archive" }),
                ("content directory", sub => sub with { ContentDirectory = "other-directory" })
            };

            foreach ((string label, Func<SubAssetEntry, SubAssetEntry> mutate) in mutations)
            {
                SubAssetEntry foreign = mutate(canonical);
                AssertThrowsContaining<InvalidOperationException>(() => ParentPayloadBuilder.BuildAll(
                    [CreateSubfileChange(parent, foreign, [9, 9])]),
                    "does not match canonical parent subentry",
                    $"same-name foreign subentry {label}");
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void DuplicateSubentryReferenceThrows()
    {
        AssetEntry parent = CreateParent("unused", [CreateSub("unused", "part.bin", 0, 1)], 1);
        SubAssetEntry sub = parent.SubEntries![0];
        AssertThrows<InvalidOperationException>(() => ParentPayloadBuilder.BuildAll(
        [
            CreateSubfileChange(parent, sub, [1]),
            CreateSubfileChange(parent, sub, [2])
        ]), "duplicate subentry reference");
    }

    private static void DuplicateSubentryNameIgnoringCaseThrows()
    {
        AssetEntry parent = CreateParent("unused", [CreateSub("unused", "part.bin", 0, 1)], 1);
        SubAssetEntry differentlyCased = CreateSub("unused", "PART.BIN", 0, 1);
        AssertThrows<InvalidOperationException>(() => ParentPayloadBuilder.BuildAll(
        [
            CreateSubfileChange(parent, parent.SubEntries![0], [1]),
            CreateSubfileChange(parent, differentlyCased, [2])
        ]), "duplicate case-insensitive subentry name");
    }

    private static void PayloadOwnsReadOnlyDataAndSubentrySnapshots()
    {
        AssetEntry parent = CreateParent("unused", [CreateSub("unused", "part.bin", 0, 1)], 1);
        ParentPayload payload = ParentPayloadBuilder.BuildAll([CreateWholeParentChange(parent, [7])]).Single();

        if (((object)payload.Data) is byte[])
            throw new Exception("ParentPayload.Data must not expose a mutable byte array.");
        if (payload.SubEntries is List<NormalizedSubEntry>)
            throw new Exception("ParentPayload.SubEntries must not expose a mutable List.");
        if (payload.SubEntries is not IList<NormalizedSubEntry> subEntries)
            throw new Exception("ParentPayload.SubEntries must expose a read-only IList snapshot.");

        AssertThrows<NotSupportedException>(() => subEntries.Add(
            new NormalizedSubEntry("extra.bin", 1, 0)), "read-only subentry snapshot");
    }

    private static void AssertParentIdentityMismatch(Func<AssetEntry, AssetEntry> mutate, string label)
    {
        string directory = CreateTempDirectory();
        try
        {
            byte[] sourceBytes = [1, 2, 3, 4, 5];
            File.WriteAllBytes(Path.Combine(directory, "synthetic.dat"), sourceBytes);
            AssetEntry first = CreateParent(directory,
            [
                CreateSub(directory, "first.bin", 0, 2),
                CreateSub(directory, "second.bin", 2, 3)
            ], sourceBytes.LongLength);
            AssetEntry second = mutate(first);

            AssertThrows<InvalidDataException>(() => ParentPayloadBuilder.BuildAll(
            [
                CreateSubfileChange(first, first.SubEntries![0], [8, 8]),
                CreateSubfileChange(second, second.SubEntries![1], [9, 9, 9])
            ]), $"parent identity {label}");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static AssetEntry CreateParent(string directory, IReadOnlyList<SubAssetEntry> subEntries, long size) => new()
    {
        AssetID = new UUID { a = 1, b = 2, c = 3, d = 4 },
        Path = "Assets/Test/Parent",
        Type = AssetType.StaticMesh,
        Offset = 0,
        Size = size,
        CompressedSize = 0,
        SourceArchive = "synthetic",
        ContentDirectory = directory,
        StoragePath = "Assets/Files/0/0/0/00000001-00000002-00000003-00000004/asset.bin",
        CsvUnknown = "7",
        CsvMetadata = "1/1/1/",
        SubEntries = subEntries
    };

    private static SubAssetEntry CreateSub(string directory, string name, long offset, long size, bool isGlobal = false) => new()
    {
        FileName = name,
        Offset = offset,
        Size = size,
        CompressedSize = 0,
        SourceArchive = "synthetic",
        ContentDirectory = directory,
        IsCompressed = false,
        IsGlobal = isGlobal
    };

    private static ModifiedAssetEntry CreateWholeParentChange(AssetEntry parent, byte[] data) => new()
    {
        ParentEntry = parent,
        SubEntry = null,
        ModifiedData = data,
        OriginalData = [],
        Compress = false
    };

    private static ModifiedAssetEntry CreateSubfileChange(AssetEntry parent, SubAssetEntry subEntry, byte[] data) => new()
    {
        ParentEntry = parent,
        SubEntry = subEntry,
        ModifiedData = data,
        OriginalData = [],
        Compress = false
    };

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ronin-parent-payload-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void AssertSubEntry(NormalizedSubEntry actual, string fileName, long offset, long size, bool isGlobal)
    {
        AssertEqual(fileName, actual.FileName, $"{fileName} name");
        AssertEqual(offset, actual.Offset, $"{fileName} offset");
        AssertEqual(size, actual.Size, $"{fileName} size");
        AssertEqual(isGlobal, actual.IsGlobal, $"{fileName} global flag");
    }

    private static void AssertSequenceEqual(byte[] expected, ReadOnlyMemory<byte> actual, string label)
    {
        if (!actual.Span.SequenceEqual(expected))
            throw new Exception($"Unexpected {label}: expected [{string.Join(',', expected)}], got [{string.Join(',', actual.ToArray())}].");
    }

    private static void AssertEqual<T>(T expected, T actual, string label) where T : notnull
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new Exception($"Unexpected {label}: expected '{expected}', got '{actual}'.");
    }

    private static void AssertThrows<TException>(Action action, string label) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new Exception($"Expected {typeof(TException).Name} for {label}.");
    }

    private static void AssertThrowsContaining<TException>(Action action, string messagePart, string label) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException ex)
        {
            if (!ex.Message.Contains(messagePart, StringComparison.Ordinal))
                throw new Exception($"Unexpected {typeof(TException).Name} for {label}: {ex.Message}");
            return;
        }

        throw new Exception($"Expected {typeof(TException).Name} for {label}.");
    }
}
