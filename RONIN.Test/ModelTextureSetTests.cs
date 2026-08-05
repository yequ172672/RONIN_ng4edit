using System.Security.Cryptography;
using System.Buffers.Binary;
using YakumoLib;
using YakumoLib.Assets;
using YakumoLib.Formats;
using YakumoLib.Modding;
using BCnEncoder.Decoder;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;

public static class ModelTextureSetTests
{
    public static void Run()
    {
        var model = Entry("Assets/Character/PL/PL0000/Model/DDX1/PL0000_DDX1", AssetType.SkeletalMesh, "00000001-00000000-00000000-00000000");
        var entries = new[] { Entry("assets/character/pl/pl0000/model/ddx1/texture/pl0000_ddx1_Normal", AssetType.Texture, "00000003-00000000-00000000-00000000"), Entry("Assets/Character/PL/PL0000/Model/DDX1/Texture/PL0000_DDX1_BaseCol", AssetType.Texture, "00000002-00000000-00000000-00000000"), Entry("Assets/Character/PL/PL0000/Model/DDX1/Texture/PL0000_DDX1_BaseColAlias", AssetType.Texture, "00000002-00000000-00000000-00000000"), Entry("Assets/Character/PL/PL0000/Model/DDX1/Texture/PL0000_DDX1_MRO", AssetType.Texture, "00000004-00000000-00000000-00000000"), Entry("Assets/Character/PL/PL0000/Model/DDX1/Texture/PL0000_DDX1_Mask", AssetType.Texture, "00000005-00000000-00000000-00000000"), Entry("Assets/Character/PL/PL0000/Model/DDX1/Materials/PL0000_DDX1_BaseCol", AssetType.Texture, "00000006-00000000-00000000-00000000"), Entry("Assets/Character/PL/PL0000/Model/DDX1/Texture/PL0000_DDX1_BaseCol.mesh", AssetType.StaticMesh, "00000007-00000000-00000000-00000000") };
        var candidates = ModelTextureSetService.DiscoverCandidates(model, entries);
        if (candidates.Count != 4 || candidates[0].Asset.FileName != "PL0000_DDX1_BaseCol" || candidates[0].UsageHint != "BaseCol" || candidates[1].UsageHint != "MRO" || candidates[2].UsageHint != "Mask" || candidates[3].UsageHint != "Normal") throw new Exception("Candidate discovery contract failed.");
        if (ModelTextureSetService.ClassifyUsage("x_ColorMask.dds") != "ColorMask" || ModelTextureSetService.ClassifyUsage("x_other.dds") != "Unknown" || ModelTextureSetService.ClassifyUsage("PL0000_DDX1_Cloth1_BaseCol_AAA3.dds") != "BaseCol" || ModelTextureSetService.ClassifyUsage("PL0000_DDX1_Normal_NNNx.dds") != "Normal" || ModelTextureSetService.ClassifyUsage("PL0000_DDX1_Mask_MROx.dds") != "MRO" || ModelTextureSetService.ClassifyUsage("PL0000_DDX1_ColorMask_BBBB.dds") != "ColorMask" || ModelTextureSetService.ClassifyUsage("PL0000_DDX1_IridescentMask_AAAx.dds") != "Mask") throw new Exception("Usage classification failed.");
        var timestamp = DateTimeOffset.Parse("2026-07-15T10:20:30Z");
        var manifest = new ModelTextureSetManifest(1, model.StringAssetID, model.Path, timestamp, candidates.Select(ModelTextureSetService.ToManifestEntry).ToArray());
        var json = ModelTextureSetService.SerializeManifest(manifest);
        if (!json.Contains("\"schemaVersion\"") || !json.Contains("\"exportedAtUtc\"")) throw new Exception("Manifest naming failed.");
        var roundTrip = ModelTextureSetService.DeserializeManifest(json);
        if (roundTrip.SchemaVersion != manifest.SchemaVersion || roundTrip.ModelAssetId != manifest.ModelAssetId || roundTrip.ModelPath != manifest.ModelPath || roundTrip.ExportedAtUtc != timestamp || roundTrip.Textures.Count != manifest.Textures.Count || roundTrip.Textures.Zip(manifest.Textures).Any(pair => pair.First != pair.Second)) throw new Exception("Manifest round-trip failed.");
        TestNg4HeaderWithoutPitchMetadataAssembly();
        TestNg4PackageHeaderNegativeMatrix();
        TestMipCountBounds();
        TestStandaloneDdsWithoutPitchMetadataRejected();
        TestDx10PaddedMipAssembly();
        TestSupportedMipFormats();
        TestBc7MipGeneration();
        TestSrgbMipFiltering();
        TestBc5NormalFiltering();
        TestRootFormatTranscoding();
        TestChangedOnlyImportPlanning();
        TestTgaExportAndImportPlanning();
        TestRealExport();
        Console.WriteLine("Model texture set focused tests passed.");
    }

    private static void TestSupportedMipFormats()
    {
        foreach ((int format, int rootLength) in new[] { (71, 32), (72, 32), (77, 64), (78, 64), (80, 32), (83, 64), (98, 64), (99, 64) })
        {
            byte[] root = new byte[148 + rootLength];
            CreateDx10Header(8, 8, 1, format, rootLength).CopyTo(root, 0);
            root.AsSpan(148).Fill(0x45);
            byte[] full = TextureMipChainGenerator.Generate(root, 4);
            int blockBytes = format is 71 or 72 or 80 ? 8 : 16;
            int expectedLength = 148 + rootLength + blockBytes * 3;
            if (full.Length != expectedLength || BinaryPrimitives.ReadUInt32LittleEndian(full.AsSpan(28, 4)) != 4 || BinaryPrimitives.ReadUInt32LittleEndian(full.AsSpan(128, 4)) != format)
                throw new Exception($"DXGI {format} mip generation contract failed.");
        }
    }

    private static void TestBc7MipGeneration()
    {
        byte[] rgba = Enumerable.Repeat(new byte[] { 210, 40, 90, 255 }, 16).SelectMany(value => value).ToArray();
        byte[] full = TextureMipChainGenerator.Generate(CreateRootDds(4, 4, 99, rgba), 2);
        TexturePackageImportResult split = TexturePackageDds.SplitForImport(full);
        if (split.MipPayloads.Count != 2 || split.MipPayloads.Any(payload => payload.Length != 16))
            throw new Exception("BC7 mip payload lengths are incorrect.");
        ColorRgba32 pixel = new BcDecoder().DecodeRaw(split.MipPayloads[0], 4, 4, CompressionFormat.Bc7)[0];
        if (pixel.r < 150 || pixel.g > 100 || pixel.b < 50)
            throw new Exception("BC7 mip generation did not preserve the source color.");
    }

    private static void TestSrgbMipFiltering()
    {
        byte[] rgba = new byte[4 * 4 * 4];
        for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++)
        {
            byte value = (x + y) % 2 == 0 ? (byte)0 : (byte)255;
            int offset = (y * 4 + x) * 4;
            rgba[offset] = rgba[offset + 1] = rgba[offset + 2] = value; rgba[offset + 3] = 255;
        }
        byte[] full = TextureMipChainGenerator.Generate(CreateRootDds(4, 4, 78, rgba), 2);
        byte[] lower = TexturePackageDds.SplitForImport(full).MipPayloads[1];
        ColorRgba32[] decoded = new BcDecoder().DecodeRaw(lower, 2, 2, CompressionFormat.Bc3);
        if (decoded.Any(pixel => pixel.r < 165 || pixel.r > 215 || Math.Abs(pixel.r - pixel.g) > 4))
            throw new Exception("sRGB mip filtering did not average color in linear space.");
    }

    private static void TestBc5NormalFiltering()
    {
        byte[] rgba = new byte[4 * 4 * 4];
        for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++)
        {
            int offset = (y * 4 + x) * 4;
            bool xNormal = (x + y) % 2 == 0;
            rgba[offset] = xNormal ? (byte)255 : (byte)128;
            rgba[offset + 1] = xNormal ? (byte)128 : (byte)255;
            rgba[offset + 2] = 0; rgba[offset + 3] = 255;
        }
        byte[] full = TextureMipChainGenerator.Generate(CreateRootDds(4, 4, 83, rgba), 2);
        byte[] lower = TexturePackageDds.SplitForImport(full).MipPayloads[1];
        ColorRgba32[] decoded = new BcDecoder().DecodeRaw(lower, 2, 2, CompressionFormat.Bc5);
        if (decoded.Any(pixel => pixel.r < 208 || pixel.g < 208 || pixel.r > 230 || pixel.g > 230))
            throw new Exception("BC5 mip filtering did not renormalize averaged normal vectors.");
    }

    private static void TestRootFormatTranscoding()
    {
        byte[] rgba = Enumerable.Repeat(new byte[] { 210, 40, 90, 255 }, 16).SelectMany(value => value).ToArray();
        byte[] bc7Payload = new BcEncoder(CompressionFormat.Bc7).EncodeToRawBytes(rgba, 4, 4, PixelFormat.Rgba32)[0];
        byte[] edited = new byte[148 + bc7Payload.Length];
        CreateDx10Header(4, 4, 1, 99, bc7Payload.Length).CopyTo(edited, 0);
        bc7Payload.CopyTo(edited, 148);
        byte[] template = CreateRootDds(4, 4, 72, new byte[4 * 4 * 4]);

        byte[] converted = TextureMipChainGenerator.TranscodeRoot(edited, template);
        if (BinaryPrimitives.ReadUInt32LittleEndian(converted.AsSpan(128, 4)) != 72 || converted.Length != template.Length)
            throw new Exception("Edited DDS was not transcoded back to the template DXGI format.");
        ColorRgba32 pixel = new BcDecoder().DecodeRaw(converted.AsSpan(148).ToArray(), 4, 4, CompressionFormat.Bc1)[0];
        if (pixel.r < 180 || pixel.g > 80 || pixel.b < 60)
            throw new Exception("DDS format transcoding did not preserve the edited pixel color.");
    }

    private static byte[] CreateRootDds(int width, int height, int dxgiFormat, byte[] rgba)
    {
        CompressionFormat format = dxgiFormat switch { 71 or 72 => CompressionFormat.Bc1, 77 or 78 => CompressionFormat.Bc3, 80 => CompressionFormat.Bc4, 83 => CompressionFormat.Bc5, 98 or 99 => CompressionFormat.Bc7, _ => throw new ArgumentOutOfRangeException(nameof(dxgiFormat)) };
        byte[] payload = new BcEncoder(format).EncodeToRawBytes(rgba, width, height, PixelFormat.Rgba32)[0];
        byte[] root = new byte[148 + payload.Length];
        CreateDx10Header(width, height, 1, dxgiFormat, payload.Length).CopyTo(root, 0);
        payload.CopyTo(root, 148);
        return root;
    }

    private static byte[] MakeVisiblePixelEdit(byte[] root)
    {
        int width = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(root.AsSpan(16, 4)));
        int height = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(root.AsSpan(12, 4)));
        int dxgi = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(root.AsSpan(128, 4)));
        CompressionFormat format = dxgi switch { 71 or 72 => CompressionFormat.Bc1, 77 or 78 => CompressionFormat.Bc3, 80 => CompressionFormat.Bc4, 83 => CompressionFormat.Bc5, _ => throw new InvalidDataException() };
        ColorRgba32[] pixels = new BcDecoder().DecodeRaw(root.AsSpan(148).ToArray(), width, height, format);
        ColorRgba32 pixel = pixels[0];
        pixels[0] = new ColorRgba32((byte)(pixel.r > 127 ? 0 : 255), pixel.g, pixel.b, pixel.a);
        byte[] rgba = pixels.SelectMany(value => new[] { value.r, value.g, value.b, value.a }).ToArray();
        byte[] payload = new BcEncoder(format).EncodeToRawBytes(rgba, width, height, PixelFormat.Rgba32)[0];
        byte[] edited = root.AsSpan(0, 148).ToArray().Concat(payload).ToArray();
        return edited.AsSpan().SequenceEqual(root) ? throw new Exception("Controlled visible pixel edit did not change DDS bytes.") : edited;
    }

    private static void TestChangedOnlyImportPlanning()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"ronin-texture-import-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            AssetEntry first = CreateSyntheticTexture(directory, "first.dat", "Texture/First", "00000010-00000000-00000000-00000000", 78);
            AssetEntry second = CreateSyntheticTexture(directory, "second.dat", "Texture/Second", "00000011-00000000-00000000-00000000", 78);
            WriteImportSet(directory, [first, second]);

            ModelTextureImportPlan unchanged = ModelTextureSetService.PlanImport(directory, [first, second]);
            if (unchanged.Changes.Count != 0 || unchanged.UnchangedCount != 2)
                throw new Exception("Unchanged texture set produced modifications.");

            string changedPath = Path.Combine(directory, "First.dds");
            byte[] changedRoot = MakeVisiblePixelEdit(File.ReadAllBytes(changedPath));
            File.WriteAllBytes(changedPath, changedRoot);
            ModelTextureImportPlan changed = ModelTextureSetService.PlanImport(directory, [first, second]);
            if (changed.Changes.Count != 1 || changed.UnchangedCount != 1)
                throw new Exception("Changed-only planning did not select exactly one texture.");
            ModifiedAssetEntry modification = changed.Changes.Single();
            if (modification.ParentEntry.AssetID != first.AssetID || modification.SubEntry is not null)
                throw new Exception("Texture import plan targeted the wrong package or a subfile.");
            TexturePackageImportResult split = TexturePackageDds.SplitForImport(modification.ModifiedData);
            if (split.MipPayloads.Count != 4 || split.MipPayloads.Select(x => x.Length).SequenceEqual([64, 16, 16, 16]) == false)
                throw new Exception("Texture import did not regenerate the complete logical mip chain.");

            byte[] canonicalManifest = File.ReadAllBytes(Path.Combine(directory, ModelTextureSetService.ManifestFileName));
            byte[] canonicalFirst = changedRoot;
            byte[] canonicalSecond = File.ReadAllBytes(Path.Combine(directory, "Second.dds"));
            void Restore()
            {
                File.WriteAllBytes(Path.Combine(directory, ModelTextureSetService.ManifestFileName), canonicalManifest);
                File.WriteAllBytes(Path.Combine(directory, "First.dds"), canonicalFirst);
                File.WriteAllBytes(Path.Combine(directory, "Second.dds"), canonicalSecond);
            }
            void Reject(string scenario, Action mutation)
            {
                Restore();
                mutation();
                AssertPlanImportThrows(directory, [first, second], scenario);
            }

            AssertPlanImportThrows(directory, [first, first], "duplicate asset UUID");
            Reject("missing DDS", () => File.Delete(Path.Combine(directory, "Second.dds")));
            Reject("mismatched UUID", () => RewriteManifest(directory, manifest => manifest with { Textures = manifest.Textures.Select((entry, index) => index == 0 ? entry with { AssetId = "00000099-00000000-00000000-00000000" } : entry).ToArray() }));
            Reject("duplicate manifest UUID", () => RewriteManifest(directory, manifest => manifest with { Textures = manifest.Textures.Select((entry, index) => index == 0 ? entry with { AssetId = manifest.Textures[1].AssetId } : entry).ToArray() }));
            Reject("mismatched logical path", () => RewriteManifest(directory, manifest => manifest with { Textures = manifest.Textures.Select((entry, index) => index == 0 ? entry with { LogicalPath = "Texture/Wrong" } : entry).ToArray() }));
            Reject("changed dimensions", () => { byte[] dds = File.ReadAllBytes(Path.Combine(directory, "First.dds")); BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(16, 4), 4); File.WriteAllBytes(Path.Combine(directory, "First.dds"), dds); });
            Reject("unsupported format", () => RewriteManifest(directory, manifest => manifest with { Textures = manifest.Textures.Select((entry, index) => index == 0 ? entry with { DxgiFormat = 97 } : entry).ToArray() }));
            Reject("invalid DDS", () => File.WriteAllBytes(Path.Combine(directory, "First.dds"), [1, 2, 3, 4]));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void TestTgaExportAndImportPlanning()
    {
        string root = Path.Combine(Path.GetTempPath(), $"ronin-texture-tga-{Guid.NewGuid():N}");
        string destination = Path.Combine(root, "export");
        string pngDestination = Path.Combine(root, "png-export");
        Directory.CreateDirectory(root);
        try
        {
            AssetEntry model = Entry("Characters/Hero", AssetType.SkeletalMesh, "00000020-00000000-00000000-00000000");
            AssetEntry texture = CreateSyntheticTexture(root, "tga-source", "Characters/Texture/Hero_BaseCol", "00000021-00000000-00000000-00000000", 78);
            AssetEntry collidingTexture = CreateSyntheticTexture(root, "tga-collision", "Characters/Texture/hero_basecol", "00000022-00000000-00000000-00000000", 78);
            try
            {
                ModelTextureSetService.ExportTga(model, [texture, collidingTexture], Path.Combine(root, "collision-export"));
                throw new Exception("Case-insensitive TGA filename collision was not rejected.");
            }
            catch (InvalidOperationException) { }

            ModelTextureSetManifest pngManifest = ModelTextureSetService.ExportPng(model, [texture], pngDestination);
            ModelTextureImportPlan pngUnchanged = ModelTextureSetService.PlanImport(pngDestination, [texture]);
            if (pngManifest.SchemaVersion != 2 || pngUnchanged.Changes.Count != 0 || pngUnchanged.UnchangedCount != 1)
                throw new Exception("Schema-2 PNG export/import regression detected.");

            ModelTextureSetManifest manifest = ModelTextureSetService.ExportTga(model, [texture], destination);
            if (manifest.SchemaVersion != 3 || manifest.Textures.Count != 1 || !manifest.Textures[0].DdsFile.EndsWith(".tga", StringComparison.OrdinalIgnoreCase))
                throw new Exception("TGA export did not create a schema-3 texture set.");

            string tgaPath = Path.Combine(destination, manifest.Textures[0].DdsFile);
            byte[] encoded = File.ReadAllBytes(tgaPath);
            if (encoded.Length < 18 || encoded[2] != 2 || encoded[16] != 32 || encoded[17] != 0x28)
                throw new Exception($"TGA export header is unexpected: type={encoded[2]}, depth={encoded[16]}, descriptor=0x{encoded[17]:X2}.");

            byte[] controlledRgba =
            [
                17, 34, 51, 0, 60, 70, 80, 90,
                100, 110, 120, 130, 140, 150, 160, 170
            ];
            byte[] controlledTga = ModelTextureSetService.EncodeTopLeftTga(controlledRgba, 2, 2);
            byte[] expectedRawBgra =
            [
                51, 34, 17, 0, 80, 70, 60, 90,
                120, 110, 100, 130, 160, 150, 140, 170
            ];
            if (controlledTga[17] != 0x28 || !controlledTga.AsSpan(18, expectedRawBgra.Length).SequenceEqual(expectedRawBgra))
                throw new Exception("TGA export did not preserve top-to-bottom rows, raw BGRA order, or transparent-pixel RGB.");
            byte[] normalizedAgain = ModelTextureSetService.NormalizeTgaToTopLeft(controlledTga.ToArray(), 2, 2);
            if (!normalizedAgain.SequenceEqual(controlledTga))
                throw new Exception("A top-left TGA was flipped a second time.");

            byte[] editedRgba = new byte[8 * 8 * 4];
            for (int i = 0; i < editedRgba.Length; i += 4)
            {
                editedRgba[i] = 17; editedRgba[i + 1] = 34; editedRgba[i + 2] = 51; editedRgba[i + 3] = 0;
            }
            byte[] validEditedTga = ModelTextureSetService.EncodeTopLeftTga(editedRgba, 8, 8);
            File.WriteAllBytes(tgaPath, validEditedTga);

            ModelTextureImportPlan changed = ModelTextureSetService.PlanImport(destination, [texture]);
            if (changed.Changes.Count != 1 || changed.UnchangedCount != 0 || changed.Changes[0].ParentEntry.AssetID != texture.AssetID)
                throw new Exception("Schema-3 TGA import did not select the edited texture.");
            byte[] importedMip0 = TexturePackageDds.SplitForImport(changed.Changes[0].ModifiedData).MipPayloads[0];
            ColorRgba32 importedTransparent = new BcDecoder().DecodeRaw(importedMip0, 8, 8, CompressionFormat.Bc3)[0];
            if (importedTransparent.a > 5 || Math.Abs(importedTransparent.r - 17) > 12 || Math.Abs(importedTransparent.g - 34) > 12 || Math.Abs(importedTransparent.b - 51) > 12)
                throw new Exception("PlanImport did not preserve transparent-pixel RGB through TGA to game-format conversion.");

            void RejectHeader(string scenario, int offset, byte value)
            {
                byte[] invalid = validEditedTga.ToArray();
                invalid[offset] = value;
                File.WriteAllBytes(tgaPath, invalid);
                AssertPlanImportThrows(destination, [texture], scenario);
            }
            RejectHeader("an RLE TGA", 2, 10);
            RejectHeader("a 24-bit TGA", 16, 24);
            RejectHeader("a bottom-left TGA", 17, 0x08);
            RejectHeader("a right-origin TGA", 17, 0x38);
            RejectHeader("a TGA with an ID field", 0, 1);
            RejectHeader("a TGA with a nonzero X origin", 8, 1);
            File.WriteAllBytes(tgaPath, validEditedTga.Concat(new byte[] { 0x42 }).ToArray());
            AssertPlanImportThrows(destination, [texture], "a TGA with trailing data");
            File.WriteAllBytes(tgaPath, validEditedTga);

            RewriteManifest(destination, current => current with
            {
                Textures = current.Textures.Select(entry => entry with { DdsFile = Path.ChangeExtension(entry.DdsFile, ".png") }).ToArray()
            });
            File.Move(tgaPath, Path.ChangeExtension(tgaPath, ".png"));
            AssertPlanImportThrows(destination, [texture], "a schema-3 texture with a PNG extension");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static void RewriteManifest(string directory, Func<ModelTextureSetManifest, ModelTextureSetManifest> mutation)
    {
        string path = Path.Combine(directory, ModelTextureSetService.ManifestFileName);
        ModelTextureSetManifest manifest = ModelTextureSetService.DeserializeManifest(File.ReadAllText(path));
        File.WriteAllText(path, ModelTextureSetService.SerializeManifest(mutation(manifest)));
    }

    private static void WriteImportSet(string directory, IReadOnlyList<AssetEntry> assets)
    {
        var entries = new List<ModelTextureSetEntry>();
        foreach (AssetEntry asset in assets)
        {
            TexturePackageDdsData root = TexturePackageDds.ExtractRootMip(asset, directory);
            string fileName = asset.FileName + ".dds";
            File.WriteAllBytes(Path.Combine(directory, fileName), root.DdsBytes);
            entries.Add(new ModelTextureSetEntry(asset.StringAssetID, asset.Path, asset.StoragePath, fileName, "Unknown",
                root.Width, root.Height, 78, root.MipCount,
                Convert.ToHexString(SHA256.HashData(root.DdsBytes)).ToLowerInvariant()));
        }
        var manifest = new ModelTextureSetManifest(1, "model", "Model", DateTimeOffset.UtcNow, entries);
        File.WriteAllText(Path.Combine(directory, ModelTextureSetService.ManifestFileName), ModelTextureSetService.SerializeManifest(manifest));
    }

    internal static AssetEntry CreateSyntheticTexture(string directory, string archiveName, string path, string id, int dxgiFormat)
    {
        int[] logical = [64, 16, 16, 16];
        int[] capacities = [80, 32, 32, 32];
        byte[] ddsHeader = CreateDx10Header(8, 8, 4, dxgiFormat, 64);
        using var image = new MemoryStream();
        using (var writer = new BinaryWriter(image, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(148 + logical.Sum());
            writer.Write(4);
            int virtualOffset = 0;
            foreach (int capacity in capacities) { writer.Write(virtualOffset); writer.Write(capacity); virtualOffset += capacity; }
            writer.Write(ddsHeader);
            writer.Write(new byte[12]);
        }
        using var archive = new MemoryStream();
        var subs = new List<SubAssetEntry>();
        byte[] imageBytes = image.ToArray();
        archive.Write(imageBytes);
        subs.Add(new SubAssetEntry { FileName = "Image.img", Offset = 0, Size = imageBytes.Length, CompressedSize = 0, ContentDirectory = directory, SourceArchive = archiveName, IsGlobal = true, IsCompressed = false });
        for (int i = 0; i < capacities.Length; i++)
        {
            long offset = archive.Position;
            byte[] payload = Enumerable.Repeat((byte)(0x30 + i), capacities[i]).ToArray();
            archive.Write(payload);
            subs.Add(new SubAssetEntry { FileName = $"mip{i}.img", Offset = offset, Size = capacities[i], CompressedSize = 0, ContentDirectory = directory, SourceArchive = archiveName, IsGlobal = true, IsCompressed = false });
        }
        long metadataOffset = archive.Position;
        archive.Write([1, 2, 3, 4]);
        subs.Add(new SubAssetEntry { FileName = "Metadata.bin", Offset = metadataOffset, Size = 4, CompressedSize = 0, ContentDirectory = directory, SourceArchive = archiveName, IsGlobal = true, IsCompressed = false });
        byte[] parentBytes = archive.ToArray();
        File.WriteAllBytes(Path.Combine(directory, archiveName + ".dat"), parentBytes);
        return Entry(path, AssetType.Texture, id) with
        {
            SubEntries = subs, ContentDirectory = directory, StoragePath = path + ".bin",
            SourceArchive = archiveName, Offset = 0, Size = parentBytes.Length, CompressedSize = 0, Pruned = false
        };
    }

    private static void AssertPlanImportThrows(string directory, IReadOnlyList<AssetEntry> assets, string scenario)
    {
        try { _ = ModelTextureSetService.PlanImport(directory, assets); }
        catch (InvalidDataException) { return; }
        throw new Exception($"Texture import did not reject {scenario}.");
    }

    private static void TestDx10PaddedMipAssembly()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"ronin-texture-dds-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            const int headerLength = 148;
            int[] logicalLengths = [64, 16, 16, 16];
            int[] capacities = [64, 32, 32, 32];
            byte[] ddsHeader = CreateDx10Header(8, 8, 4, 78, logicalLengths[0]);
            using var packageHeader = new MemoryStream();
            using (var writer = new BinaryWriter(packageHeader, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(headerLength + logicalLengths.Sum());
                writer.Write(logicalLengths.Length);
                int offset = 0;
                foreach (int capacity in capacities)
                {
                    writer.Write(offset);
                    writer.Write(capacity);
                    offset += capacity;
                }
                writer.Write(ddsHeader);
            }

            using var archive = new MemoryStream();
            var subEntries = new List<SubAssetEntry>();
            archive.Write(packageHeader.ToArray());
            subEntries.Add(new SubAssetEntry { FileName = "Image.img", Size = packageHeader.Length, CompressedSize = 0, Offset = 0, SourceArchive = "synthetic", ContentDirectory = directory, IsCompressed = false, IsGlobal = true });
            for (int i = 0; i < capacities.Length; i++)
            {
                long offset = archive.Position;
                byte value = checked((byte)(0x20 + i));
                archive.Write(Enumerable.Repeat(value, capacities[i]).Select(x => (byte)x).ToArray());
                subEntries.Add(new SubAssetEntry { FileName = $"mip{i}.img", Size = capacities[i], CompressedSize = 0, Offset = offset, SourceArchive = "synthetic", ContentDirectory = directory, IsCompressed = false, IsGlobal = true });
            }
            File.WriteAllBytes(Path.Combine(directory, "synthetic.dat"), archive.ToArray());
            var asset = Entry("SyntheticTexture", AssetType.Texture, "00000008-00000000-00000000-00000000") with { SubEntries = subEntries };

            TexturePackageDdsData full = TexturePackageDds.Assemble(asset, packageHeader.ToArray(), directory);
            if (full.HeaderLength != 148 || full.TotalSize != 260 || full.DdsBytes.Length != 260)
                throw new Exception($"DX10 logical DDS assembly failed: header={full.HeaderLength}, total={full.TotalSize}, bytes={full.DdsBytes.Length}.");
            int cursor = headerLength;
            for (int i = 0; i < logicalLengths.Length; i++)
            {
                if (full.DdsBytes.AsSpan(cursor, logicalLengths[i]).ToArray().Any(value => value != 0x20 + i))
                    throw new Exception($"Logical mip {i} payload was not preserved.");
                cursor += logicalLengths[i];
            }
            TexturePackageDdsData root = TexturePackageDds.ExtractRootMip(asset, directory);
            if (root.HeaderLength != 148 || root.DdsBytes.Length != 212 || root.TotalSize != 212 || BinaryPrimitives.ReadUInt32LittleEndian(root.DdsBytes.AsSpan(28, 4)) != 1)
                throw new Exception("Synthetic DX10 root-mip extraction did not preserve the dynamic header and exact logical payload.");
            if (!root.DdsBytes.AsSpan(148, 64).SequenceEqual(Enumerable.Repeat((byte)0x20, 64).ToArray()))
                throw new Exception("Synthetic DX10 root-mip bytes are incorrect.");

            byte[] singleDdsHeader = CreateDx10Header(8, 8, 0, 78, logicalLengths[0]);
            using var singlePackageHeader = new MemoryStream();
            using (var writer = new BinaryWriter(singlePackageHeader, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(headerLength + logicalLengths[0]);
                writer.Write(1);
                writer.Write(0);
                writer.Write(capacities[0]);
                writer.Write(singleDdsHeader);
            }
            TexturePackageDdsData normalizedSingle = TexturePackageDds.Assemble(asset, singlePackageHeader.ToArray(), directory);
            if (normalizedSingle.MipCount != 1 || normalizedSingle.DdsBytes.Length != 212)
                throw new Exception("A DDS raw mip count of zero was not normalized to one level.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void TestNg4HeaderWithoutPitchMetadataAssembly()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"ronin-ng4-dds-header-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            const int headerLength = 148;
            int[] logicalLengths = [64, 16, 16, 16];
            int[] capacities = [64, 32, 32, 32];
            byte[] ddsHeader = CreateDx10Header(8, 8, 4, 99, logicalLengths[0]);
            BinaryPrimitives.WriteUInt32LittleEndian(ddsHeader.AsSpan(8), 0x00021007);
            BinaryPrimitives.WriteUInt32LittleEndian(ddsHeader.AsSpan(20), 0);

            using var packageHeader = new MemoryStream();
            using (var writer = new BinaryWriter(packageHeader, System.Text.Encoding.UTF8, leaveOpen: true))
            {
                writer.Write(headerLength + logicalLengths.Sum());
                writer.Write(logicalLengths.Length);
                int virtualOffset = 0;
                foreach (int capacity in capacities)
                {
                    writer.Write(virtualOffset);
                    writer.Write(capacity);
                    virtualOffset += capacity;
                }
                writer.Write(ddsHeader);
            }

            using var archive = new MemoryStream();
            var subEntries = new List<SubAssetEntry>();
            archive.Write(packageHeader.ToArray());
            subEntries.Add(new SubAssetEntry { FileName = "Image.img", Size = packageHeader.Length, CompressedSize = 0, Offset = 0, SourceArchive = "ng4-header", ContentDirectory = directory, IsCompressed = false, IsGlobal = true });
            for (int i = 0; i < capacities.Length; i++)
            {
                long offset = archive.Position;
                archive.Write(Enumerable.Repeat(checked((byte)(0x50 + i)), capacities[i]).ToArray());
                subEntries.Add(new SubAssetEntry { FileName = $"mip{i}.img", Size = capacities[i], CompressedSize = 0, Offset = offset, SourceArchive = "ng4-header", ContentDirectory = directory, IsCompressed = false, IsGlobal = true });
            }
            File.WriteAllBytes(Path.Combine(directory, "ng4-header.dat"), archive.ToArray());
            var asset = Entry("Ng4HeaderWithoutPitchMetadata", AssetType.Texture, "0000000A-00000000-00000000-00000000") with { SubEntries = subEntries };

            TexturePackageDdsData assembled = TexturePackageDds.Assemble(asset, packageHeader.ToArray(), directory);
            uint outputFlags = BinaryPrimitives.ReadUInt32LittleEndian(assembled.DdsBytes.AsSpan(8));
            uint outputLinearSize = BinaryPrimitives.ReadUInt32LittleEndian(assembled.DdsBytes.AsSpan(20));
            if ((outputFlags & 0x00080000) == 0 || outputLinearSize != logicalLengths[0])
                throw new Exception("NG4 package DDS extraction did not normalize standalone linear-size metadata.");
            if (assembled.DdsBytes.Length != headerLength + logicalLengths.Sum() || assembled.MipCount != logicalLengths.Length ||
                BinaryPrimitives.ReadUInt32LittleEndian(assembled.DdsBytes.AsSpan(128)) != 99)
                throw new Exception("NG4 package DDS extraction changed the self-consistent BC7 layout.");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void TestStandaloneDdsWithoutPitchMetadataRejected()
    {
        byte[] dds = new byte[148 + 64];
        CreateDx10Header(8, 8, 1, 99, 64).CopyTo(dds, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(8), 0x00001007);
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(20), 0);
        try
        {
            _ = TexturePackageDds.SplitForImport(dds);
        }
        catch (InvalidDataException)
        {
            return;
        }
        throw new Exception("Standalone DDS import accepted missing pitch/linear-size metadata.");
    }

    private static void TestNg4PackageHeaderNegativeMatrix()
    {
        int[] logicalLengths = [64, 16, 16, 16];
        int[] capacities = [64, 32, 32, 32];
        using SyntheticTexturePackage valid = CreateSyntheticTexturePackage(
            8, 8, logicalLengths, capacities, 0x00021007, 0);
        int ddsOffset = 8 + logicalLengths.Length * 8;

        byte[] missingFlagWithValue = valid.HeaderBlob.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(missingFlagWithValue.AsSpan(ddsOffset + 20), 64);
        AssertAssembleThrows(valid.Asset, missingFlagWithValue, valid.Directory, "missing pitch flag with a nonzero value");

        byte[] presentFlagWithZeroValue = valid.HeaderBlob.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(presentFlagWithZeroValue.AsSpan(ddsOffset + 8), 0x000A1007);
        AssertAssembleThrows(valid.Asset, presentFlagWithZeroValue, valid.Directory, "present linear-size flag with a zero value");

        byte[] wrongTotal = valid.HeaderBlob.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(wrongTotal, checked((uint)(148 + logicalLengths.Sum() + 16)));
        AssertAssembleThrows(valid.Asset, wrongTotal, valid.Directory, "a package total inconsistent with derived mip lengths");

        using SyntheticTexturePackage undersized = CreateSyntheticTexturePackage(
            8, 8, logicalLengths, [63, 32, 32, 32], 0x00021007, 0);
        AssertAssembleThrows(undersized.Asset, undersized.HeaderBlob, undersized.Directory, "a descriptor capacity smaller than its logical mip");
    }

    private static void TestMipCountBounds()
    {
        int[] legalLengths = [64, 16, 16, 16];
        using (SyntheticTexturePackage legal = CreateSyntheticTexturePackage(
            8, 8, legalLengths, legalLengths, 0x000A1007, 64))
        {
            TexturePackageDdsData assembled = TexturePackageDds.Assemble(legal.Asset, legal.HeaderBlob, legal.Directory);
            if (assembled.MipCount != 4)
                throw new Exception("The dimension-derived maximum legal mip count was rejected.");
        }

        int[] excessiveLengths = [64, 16, 16, 16, 16];
        using (SyntheticTexturePackage excessive = CreateSyntheticTexturePackage(
            8, 8, excessiveLengths, excessiveLengths, 0x000A1007, 64))
            AssertAssembleThrows(excessive.Asset, excessive.HeaderBlob, excessive.Directory, "a mip count beyond the dimension-derived chain");

        int[] wrappedLengths = Enumerable.Range(0, 33)
            .Select(level =>
            {
                int mipWidth = Math.Max(1, 8 >> level);
                int mipHeight = Math.Max(1, 8 >> level);
                return Math.Max(1, (mipWidth + 3) / 4) * Math.Max(1, (mipHeight + 3) / 4) * 16;
            })
            .ToArray();
        using (SyntheticTexturePackage wrapped = CreateSyntheticTexturePackage(
            8, 8, wrappedLengths, wrappedLengths, 0x000A1007, 64))
            AssertAssembleThrows(wrapped.Asset, wrapped.HeaderBlob, wrapped.Directory, "a crafted mip count above the 32-bit shift boundary");

        byte[] standalone = new byte[148 + wrappedLengths.Sum()];
        CreateDx10Header(8, 8, wrappedLengths.Length, 99, 64).CopyTo(standalone, 0);
        AssertSplitForImportThrows(standalone, "a standalone DDS mip count above the legal chain");
    }

    private static void AssertAssembleThrows(AssetEntry asset, byte[] headerBlob, string directory, string scenario)
    {
        try
        {
            _ = TexturePackageDds.Assemble(asset, headerBlob, directory);
        }
        catch (InvalidDataException)
        {
            return;
        }
        throw new Exception($"Texture package assembly accepted {scenario}.");
    }

    private static void AssertSplitForImportThrows(byte[] dds, string scenario)
    {
        try
        {
            _ = TexturePackageDds.SplitForImport(dds);
        }
        catch (InvalidDataException)
        {
            return;
        }
        throw new Exception($"Standalone DDS import accepted {scenario}.");
    }

    private static SyntheticTexturePackage CreateSyntheticTexturePackage(
        int width,
        int height,
        int[] logicalLengths,
        int[] capacities,
        uint flags,
        uint pitchOrLinearSize)
    {
        if (logicalLengths.Length != capacities.Length)
            throw new ArgumentException("Synthetic mip lengths and capacities must match.");
        string directory = Path.Combine(Path.GetTempPath(), $"ronin-dds-package-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        const string archiveName = "synthetic-package";
        byte[] ddsHeader = CreateDx10Header(width, height, logicalLengths.Length, 99, checked((int)pitchOrLinearSize));
        BinaryPrimitives.WriteUInt32LittleEndian(ddsHeader.AsSpan(8), flags);
        BinaryPrimitives.WriteUInt32LittleEndian(ddsHeader.AsSpan(20), pitchOrLinearSize);

        using var packageHeader = new MemoryStream();
        using (var writer = new BinaryWriter(packageHeader, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(checked(148 + logicalLengths.Sum()));
            writer.Write(logicalLengths.Length);
            int virtualOffset = 0;
            foreach (int capacity in capacities)
            {
                writer.Write(virtualOffset);
                writer.Write(capacity);
                virtualOffset = checked(virtualOffset + capacity);
            }
            writer.Write(ddsHeader);
        }

        using var archive = new MemoryStream();
        var subEntries = new List<SubAssetEntry>();
        byte[] headerBlob = packageHeader.ToArray();
        archive.Write(headerBlob);
        subEntries.Add(new SubAssetEntry { FileName = "Image.img", Size = headerBlob.Length, CompressedSize = 0, Offset = 0, SourceArchive = archiveName, ContentDirectory = directory, IsCompressed = false, IsGlobal = true });
        for (int index = 0; index < capacities.Length; index++)
        {
            long offset = archive.Position;
            archive.Write(new byte[capacities[index]]);
            subEntries.Add(new SubAssetEntry { FileName = $"mip{index}.img", Size = capacities[index], CompressedSize = 0, Offset = offset, SourceArchive = archiveName, ContentDirectory = directory, IsCompressed = false, IsGlobal = true });
        }
        File.WriteAllBytes(Path.Combine(directory, archiveName + ".dat"), archive.ToArray());
        AssetEntry asset = Entry("SyntheticTexturePackage", AssetType.Texture, "0000000B-00000000-00000000-00000000") with { SubEntries = subEntries };
        return new SyntheticTexturePackage(directory, asset, headerBlob);
    }

    private sealed record SyntheticTexturePackage(string Directory, AssetEntry Asset, byte[] HeaderBlob) : IDisposable
    {
        public void Dispose() => System.IO.Directory.Delete(Directory, recursive: true);
    }

    private static byte[] CreateDx10Header(int width, int height, int mipCount, int dxgiFormat, int linearSize)
    {
        byte[] header = new byte[148];
        "DDS "u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 124);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), 0x000A1007);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(12), checked((uint)height));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), checked((uint)width));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20), checked((uint)linearSize));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(28), checked((uint)mipCount));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(76), 32);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(80), 4);
        "DX10"u8.CopyTo(header.AsSpan(84));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(108), 0x00401008);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(128), checked((uint)dxgiFormat));
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(132), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(140), 1);
        return header;
    }

    private static void TestRealExport()
    {
        string? assetsDirectory = Environment.GetEnvironmentVariable("RONIN_NG4_ASSETS");
        if (string.IsNullOrWhiteSpace(assetsDirectory) || !Directory.Exists(assetsDirectory))
        {
            Console.WriteLine("Real texture export tests skipped; set RONIN_NG4_ASSETS to a game Assets directory to enable them.");
            return;
        }
        const string modelPath = "Assets/Character/PL/PL0000/Model/DDX1/PL0000_DDX1";
        const string textureRoot = "Assets/Character/PL/PL0000/Model/DDX1/Texture/";
        string destination = Path.Combine(Path.GetTempPath(), $"ronin-texture-set-{Guid.NewGuid():N}");

        try
        {
            AssetLibrary library = AssetLibrary.Load(assetsDirectory);
            AssetEntry model = library.All.Single(entry => entry.Path.Equals(modelPath, StringComparison.OrdinalIgnoreCase));
            TestTransactionalPreflight(model, library.All, destination);
            ModelTextureSetManifest exported = ModelTextureSetService.Export(model, library.All, destination);
            string manifestPath = Path.Combine(destination, ModelTextureSetService.ManifestFileName);
            if (!File.Exists(manifestPath)) throw new Exception("Export manifest was not created.");

            ModelTextureSetManifest persisted = ModelTextureSetService.DeserializeManifest(File.ReadAllText(manifestPath));
            if (persisted.SchemaVersion != exported.SchemaVersion || persisted.ModelAssetId != exported.ModelAssetId || persisted.ModelPath != exported.ModelPath || persisted.Textures.Count != exported.Textures.Count || persisted.Textures.Zip(exported.Textures).Any(pair => pair.First != pair.Second) || persisted.Textures.Count == 0) throw new Exception("Persisted export manifest does not match the result.");
            HashSet<string> materialTextureIds = Ng4MaterialResolver.ResolveTextureDependencies(model, library.All)
                .Select(entry => entry.StringAssetID)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (persisted.Textures.Any(entry =>
                    !entry.LogicalPath.Replace('\\', '/').StartsWith(textureRoot, StringComparison.OrdinalIgnoreCase) &&
                    !materialTextureIds.Contains(entry.AssetId)))
                throw new Exception("Export included a texture that was neither model-adjacent nor an exact material dependency.");
            if (persisted.Textures.Select(entry => entry.DdsFile).Distinct(StringComparer.OrdinalIgnoreCase).Count() != persisted.Textures.Count) throw new Exception("Exported DDS filenames are not unique.");

            foreach (ModelTextureSetEntry entry in persisted.Textures)
            {
                if (entry.DdsFile != Path.GetFileName(entry.DdsFile) || entry.DdsFile is "." or ".." || !entry.DdsFile.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)) throw new Exception($"Unsafe DDS filename '{entry.DdsFile}'.");
                string ddsPath = Path.Combine(destination, entry.DdsFile);
                if (!File.Exists(ddsPath)) throw new Exception($"Missing exported DDS '{entry.DdsFile}'.");
                byte[] dds = File.ReadAllBytes(ddsPath);
                if (dds.Length < 4 || !dds.AsSpan(0, 4).SequenceEqual("DDS "u8)) throw new Exception($"Invalid DDS signature for '{entry.DdsFile}'.");
                if (BinaryPrimitives.ReadUInt32LittleEndian(dds.AsSpan(28, 4)) != 1) throw new Exception($"Exported DDS '{entry.DdsFile}' is not root-mip-only.");
                if (entry.Width <= 0 || entry.Height <= 0 || entry.MipCount <= 0) throw new Exception($"Invalid DDS metadata for '{entry.DdsFile}'.");
                AssetEntry source = library.All.Single(asset => asset.Path.Equals(entry.LogicalPath, StringComparison.OrdinalIgnoreCase));
                SubAssetEntry image = source.SubEntries!.Single(sub => sub.FileName.Equals("Image.img", StringComparison.OrdinalIgnoreCase));
                byte[] imageBytes = AssetExtractor.GetSubBlob(image, source, image.ContentDirectory);
                int sourceMipCount = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(imageBytes.AsSpan(4, 4)));
                if (entry.MipCount != sourceMipCount) throw new Exception($"Manifest mip count for '{entry.LogicalPath}' is {entry.MipCount}, source declares {sourceMipCount}.");
                string sha256 = Convert.ToHexString(SHA256.HashData(dds)).ToLowerInvariant();
                if (!entry.OriginalSha256.Equals(sha256, StringComparison.OrdinalIgnoreCase)) throw new Exception($"SHA-256 mismatch for '{entry.DdsFile}'.");
            }

            ModelTextureSetEntry baseColor = persisted.Textures.Single(entry => entry.LogicalPath.EndsWith("/PL0000_DDX1_Cloth1_BaseCol_AAA3", StringComparison.OrdinalIgnoreCase));
            byte[] rootDds = File.ReadAllBytes(Path.Combine(destination, baseColor.DdsFile));
            if (baseColor.Width != 4096 || baseColor.Height != 4096 || baseColor.MipCount != 13 || BinaryPrimitives.ReadUInt32LittleEndian(rootDds.AsSpan(128, 4)) != 78 || BinaryPrimitives.ReadUInt32LittleEndian(rootDds.AsSpan(28, 4)) != 1)
                throw new Exception("Real DDX1 BaseCol root-mip export metadata is incorrect.");
            AssetEntry baseColorAsset = library.All.Single(entry => entry.Path.Equals(baseColor.LogicalPath, StringComparison.OrdinalIgnoreCase));
            string baseColorContentDirectory = baseColorAsset.SubEntries!.First(entry => entry.FileName.Equals("Image.img", StringComparison.OrdinalIgnoreCase)).ContentDirectory;
            TexturePackageDdsData fullBaseColor = TexturePackageDds.Extract(baseColorAsset, baseColorContentDirectory);
            if (fullBaseColor.TotalSize != 22_369_796 || fullBaseColor.DdsBytes.Length != 22_369_796 || fullBaseColor.MipCount != 13 || fullBaseColor.HeaderLength != 148)
                throw new Exception("Real DDX1 BaseCol full DDS logical layout is incorrect.");

            ModelTextureSetEntry smallest = persisted.Textures
                .Where(entry => entry.DxgiFormat is 71 or 72 or 77 or 78 or 80 or 83)
                .OrderBy(entry => new FileInfo(Path.Combine(destination, entry.DdsFile)).Length)
                .First();
            string smallestPath = Path.Combine(destination, smallest.DdsFile);
            File.WriteAllBytes(smallestPath, MakeVisiblePixelEdit(File.ReadAllBytes(smallestPath)));
            ModelTextureImportPlan realPlan = ModelTextureSetService.PlanImport(destination, library.All);
            if (realPlan.Changes.Count != 1 || realPlan.Changes[0].ParentEntry.StringAssetID != smallest.AssetId)
                throw new Exception("Real smallest-candidate smoke did not select exactly the edited texture.");
            AssetEntry smallestAsset = library.All.Single(asset => asset.StringAssetID.Equals(smallest.AssetId, StringComparison.OrdinalIgnoreCase));
            string smallestContent = smallestAsset.SubEntries!.First(sub => sub.FileName.Equals("Image.img", StringComparison.OrdinalIgnoreCase)).ContentDirectory;
            TexturePackageImportResult generatedMips = TexturePackageDds.SplitForImport(realPlan.Changes[0].ModifiedData);
            TexturePackageImportResult sourceMips = TexturePackageDds.SplitForImport(TexturePackageDds.Extract(smallestAsset, smallestContent).DdsBytes);
            if (generatedMips.MipPayloads.Count != smallest.MipCount ||
                !generatedMips.MipPayloads.Select(mip => mip.Length).SequenceEqual(sourceMips.MipPayloads.Select(mip => mip.Length)))
                throw new Exception("Real smallest-candidate smoke generated an incompatible mip layout.");
        }
        finally
        {
            if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
            string? parent = Path.GetDirectoryName(destination);
            string prefix = Path.GetFileName(destination) + ".ronin-";
            if (parent is not null && Directory.Exists(parent))
                foreach (string temp in Directory.EnumerateDirectories(parent, prefix + "*.tmp")) Directory.Delete(temp, recursive: true);
        }
    }

    private static void TestTransactionalPreflight(AssetEntry model, IEnumerable<AssetEntry> assets, string destination)
    {
        string parent = Path.GetDirectoryName(destination)!;
        string prefix = Path.GetFileName(destination) + ".ronin-";
        var malformed = Entry(Path.GetDirectoryName(model.Path)!.Replace('\\', '/') + "/Texture/" + model.FileName + "_zzzz_invalid", AssetType.Texture, "00000009-00000000-00000000-00000000");
        bool temporaryWasCreated = false;
        using var watcher = new FileSystemWatcher(parent) { IncludeSubdirectories = false, EnableRaisingEvents = true, NotifyFilter = NotifyFilters.DirectoryName };
        watcher.Created += (_, args) => { if (Path.GetFileName(args.FullPath).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) temporaryWasCreated = true; };
        try
        {
            try
            {
                ModelTextureSetService.Export(model, assets.Append(malformed), destination);
                throw new Exception("Malformed texture candidate unexpectedly exported.");
            }
            catch (InvalidDataException) { }
            watcher.WaitForChanged(WatcherChangeTypes.All, 100);
            if (Directory.Exists(destination)) throw new Exception("Failed texture-set export left its destination directory behind.");
            if (Directory.EnumerateDirectories(parent, prefix + "*.tmp").Any()) throw new Exception("Failed texture-set export leaked a temporary directory.");
            if (temporaryWasCreated) throw new Exception("Failed texture-set export created a temporary directory before all candidates passed preflight.");
        }
        finally
        {
            if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
            foreach (string temp in Directory.EnumerateDirectories(parent, prefix + "*.tmp")) Directory.Delete(temp, recursive: true);
        }
    }

    private static AssetEntry Entry(string path, AssetType type, string id) => new() { Path = path, Type = type, AssetID = UUIDParser.Parse(id) };
}
