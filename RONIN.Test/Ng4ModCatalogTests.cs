using YakumoLib;
using YakumoLib.Assets;
using YakumoLib.Database;

internal static class Ng4ModCatalogTests
{
    private const string AssetIdText = "75d246be-4ac8e807-63d20583-6eb1ac97";
    private const string StoragePath = "Assets/Files/7/5/d/75d246be-4ac8e807-63d20583-6eb1ac97/asset.bin";

    public static void Run()
    {
        HighestNumberedPatchAndLastRowWin();
        PatchNumbersAreSortedNumerically();
        UnnumberedCsvNamesUseOrdinalOrdering();
        MixedNumericAndInvalidPatchNamesUseOneStableSortKey();
        OversizedPatchNumbersAreSortedNumerically();
        Console.WriteLine("NG4MOD catalog focused tests passed.");
    }

    private static void HighestNumberedPatchAndLastRowWin()
    {
        using var fixture = new CatalogFixture();

        fixture.Write("@patch_image1.csv", Row(301));
        fixture.Write("@patch_image10.csv", Row(1_000), Row(1_010));
        fixture.Write("@image2.csv", Row(202));
        fixture.Write("@patch_image0.csv", Row(300));
        fixture.Write("@image0.csv", Row(200));

        AssetEntry result = fixture.Load();

        Assert(result.Offset == 1_010, $"Expected the last row from patch 10 to win, got offset {result.Offset}.");
        Assert(result.SourceArchive == "@patch_image10", $"Expected @patch_image10, got {result.SourceArchive}.");
    }

    private static void PatchNumbersAreSortedNumerically()
    {
        using var fixture = new CatalogFixture();

        fixture.Write("@patch_image10.csv", Row(1_010));
        fixture.Write("@patch_image2.csv", Row(302));

        AssetEntry result = fixture.Load();

        Assert(result.Offset == 1_010, $"Expected numeric patch order to place patch 10 after patch 2, got offset {result.Offset}.");
        Assert(result.SourceArchive == "@patch_image10", $"Expected @patch_image10, got {result.SourceArchive}.");
    }

    private static void UnnumberedCsvNamesUseOrdinalOrdering()
    {
        using var fixture = new CatalogFixture();

        fixture.Write("@patch_image_z.csv", Row(402));
        fixture.Write("@patch_image_a.csv", Row(401));

        AssetEntry result = fixture.Load();

        Assert(result.Offset == 402, $"Expected ordinal filename order to place _z after _a, got offset {result.Offset}.");
        Assert(result.SourceArchive == "@patch_image_z", $"Expected @patch_image_z, got {result.SourceArchive}.");
    }

    private static void MixedNumericAndInvalidPatchNamesUseOneStableSortKey()
    {
        string[][] creationOrders =
        [
            ["@patch_image9.csv", "@patch_image0x1.csv", "@patch_image5x.csv", "@patch_image2.csv", "@patch_image10.csv"],
            ["@patch_image5x.csv", "@patch_image10.csv", "@patch_image2.csv", "@patch_image0x1.csv", "@patch_image9.csv"]
        ];

        foreach (string[] creationOrder in creationOrders)
        {
            using var fixture = new CatalogFixture();

            for (int index = 0; index < creationOrder.Length; index++)
                fixture.Write(creationOrder[index], Row(500 + index));

            AssetEntry first = fixture.Load();
            AssetEntry second = fixture.Load();

            Assert(first.SourceArchive == "@patch_image5x",
                $"Expected ordinal-last invalid patch @patch_image5x, got {first.SourceArchive} for creation order {string.Join(", ", creationOrder)}.");
            Assert(second.SourceArchive == first.SourceArchive && second.Offset == first.Offset,
                $"Expected repeated catalog loads to select the same patch, got {first.SourceArchive}/{first.Offset} then {second.SourceArchive}/{second.Offset}.");
        }
    }

    private static void OversizedPatchNumbersAreSortedNumerically()
    {
        const string smaller = "@patch_image9999999999999999999.csv";
        const string larger = "@patch_image100000000000000000000.csv";
        string[][] creationOrders = [[smaller, larger], [larger, smaller]];

        foreach (string[] creationOrder in creationOrders)
        {
            using var fixture = new CatalogFixture();

            fixture.Write(creationOrder[0], Row(601));
            fixture.Write(creationOrder[1], Row(602));

            AssetEntry result = fixture.Load();

            Assert(result.SourceArchive == Path.GetFileNameWithoutExtension(larger),
                $"Expected the larger oversized patch number to win, got {result.SourceArchive} for creation order {string.Join(", ", creationOrder)}.");
        }
    }

    private static string Row(long offset) => $"{StoragePath},{offset},0,64,0,0,metadata";

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    private sealed class CatalogFixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"ronin-catalog-{Guid.NewGuid():N}");
        private readonly UUID _assetId = UUIDParser.Parse(AssetIdText);

        public CatalogFixture()
        {
            Directory.CreateDirectory(_directory);
        }

        public void Write(string fileName, params string[] rows)
        {
            File.WriteAllLines(Path.Combine(_directory, fileName), ["path,offset,unknown,size,compressed_size,file_count,metadata", .. rows]);
        }

        public AssetEntry Load()
        {
            var assets = new Dictionary<UUID, AssetEntry>
            {
                [_assetId] = new AssetEntry
                {
                    AssetID = _assetId,
                    Path = "Assets/Test/asset.bin",
                    Type = AssetType.Unknown
                }
            };

            new CsvReader().LoadAssetInfo(assets, _directory);
            return assets[_assetId];
        }

        public void Dispose()
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
