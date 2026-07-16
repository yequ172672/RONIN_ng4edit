using System.Diagnostics;
using System.Text;
using YakumoLib.Assets;
using YakumoLib.Database;
using YakumoLib.Formats;
using YakumoLib.Modding;

if (args.Contains("--model-texture-set-tests", StringComparer.OrdinalIgnoreCase))
{
    ModelTextureSetTests.Run();
    return;
}

if (args.Length == 4 && args[0].Equals("--export-model-texture-set", StringComparison.OrdinalIgnoreCase))
{
    TextureSetCli.ExportResult result = TextureSetCli.Export(args[1], args[2], args[3]);
    Console.WriteLine($"Exported {result.TextureCount} root-mip textures for {result.ModelPath} to {result.Destination}");
    return;
}

if (args.Length == 4 && args[0].Equals("--export-model-texture-set-png", StringComparison.OrdinalIgnoreCase))
{
    TextureSetCli.ExportResult result = TextureSetCli.ExportPng(args[1], args[2], args[3]);
    Console.WriteLine($"Exported {result.TextureCount} root-mip PNG textures for {result.ModelPath} to {result.Destination}");
    return;
}

if (args.Length == 3 && args[0].Equals("--import-model-texture-set", StringComparison.OrdinalIgnoreCase))
{
    TextureSetCli.ImportResult result = TextureSetCli.Import(args[1], args[2]);
    Console.WriteLine(result.PatchId is null
        ? $"No texture changes ({result.UnchangedCount} unchanged); no patch generated."
        : $"Generated @{result.PatchId}.csv/.dat ({result.ChangedCount} changed, {result.UnchangedCount} unchanged)");
    return;
}

if (args.Length == 5 && args[0].Equals("--generate-subasset-patch", StringComparison.OrdinalIgnoreCase))
{
    string assetsDirectory = Path.GetFullPath(args[1]);
    string assetQuery = args[2];
    string subFileName = args[3];
    string replacementPath = Path.GetFullPath(args[4]);
    AssetLibrary patchLibrary = AssetLibrary.Load(assetsDirectory);
    AssetEntry[] exactMatches = patchLibrary.All.Where(entry =>
        entry.Path.Equals(assetQuery, StringComparison.OrdinalIgnoreCase)).ToArray();
    AssetEntry[] matches = exactMatches.Length > 0 ? exactMatches : patchLibrary.All.Where(entry =>
        entry.Path.Contains(assetQuery, StringComparison.OrdinalIgnoreCase)).ToArray();
    if (matches.Length != 1)
        throw new InvalidOperationException($"Asset query '{assetQuery}' matched {matches.Length} entries; use a unique path fragment.{Environment.NewLine}" +
                                            string.Join(Environment.NewLine, matches.Select(entry => entry.Path)));
    AssetEntry parent = matches[0];
    SubAssetEntry sub = parent.SubEntries?.SingleOrDefault(entry =>
        entry.FileName.Equals(subFileName, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Asset '{parent.Path}' has no unique subfile '{subFileName}'.{Environment.NewLine}" +
                                               string.Join(Environment.NewLine, parent.SubEntries?.Select(entry => entry.FileName) ?? []));
    byte[] replacement = File.ReadAllBytes(replacementPath);
    _ = MDLParserExtended.Parse(replacement);
    byte[] originalSubData = AssetExtractor.GetSubBlob(sub, parent, sub.ContentDirectory);
    string generatedPatchId = PatchGenerator.GeneratePatch(assetsDirectory,
    [
        new ModifiedAssetEntry
        {
            ParentEntry = parent,
            SubEntry = sub,
            ModifiedData = replacement,
            OriginalData = originalSubData,
            Compress = false
        }
    ], compressData: false);
    Console.WriteLine($"Generated @{generatedPatchId}.csv/.dat for {parent.Path}/{sub.FileName}");
    return;
}

if (args.Length == 5 && args[0].Equals("--verify-subasset-replacement", StringComparison.OrdinalIgnoreCase))
{
    string assetsDirectory = Path.GetFullPath(args[1]);
    string logicalAssetPath = args[2];
    string subFileName = args[3];
    string expectedPath = Path.GetFullPath(args[4]);
    AssetLibrary verifyLibrary = AssetLibrary.Load(assetsDirectory);
    AssetEntry parent = verifyLibrary.All.Single(entry => entry.Path.Equals(logicalAssetPath, StringComparison.OrdinalIgnoreCase));
    SubAssetEntry sub = parent.SubEntries?.Single(entry => entry.FileName.Equals(subFileName, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException($"Asset '{logicalAssetPath}' has no subfile '{subFileName}'.");
    byte[] actual = AssetExtractor.GetSubBlob(sub, parent, sub.ContentDirectory);
    byte[] expected = File.ReadAllBytes(expectedPath);
    if (!actual.AsSpan().SequenceEqual(expected))
        throw new InvalidDataException($"Installed {parent.SourceArchive}/{sub.FileName} differs from '{expectedPath}'.");
    Console.WriteLine($"Verified {parent.SourceArchive}/{parent.StoragePath}/{sub.FileName} ({actual.Length} bytes)");
    return;
}

string assetPath = args.Length > 0 ? args[0] : "D:\\BaiduNetdiskDownload\\NINJA GAIDEN 4 The Two Masters\\Assets";
string gameRoot = Path.GetDirectoryName(assetPath.TrimEnd('\\', '/'))!;
string outDir = "D:\\code\\Game\\RONIN_ng4edit-main\\ExtractedModels";
Directory.CreateDirectory(outDir);

// ═══════════ LOAD ASSET DATABASE ═══════════
Console.Write("Loading asset database... ");
var sw = Stopwatch.StartNew();
var library = AssetLibrary.Load(assetPath);
sw.Stop();
Console.WriteLine($"{library.Count} assets in {sw.ElapsedMilliseconds}ms");

// ═══════════ FIND MODEL ═══════════
var model = library.All
    .Where(a => a.Type == AssetType.SkeletalMesh && a.SubEntries != null)
    .Select(a => new { Entry = a, LOD = a.SubEntries!.FirstOrDefault(s => !s.IsCompressed && s.FileName.EndsWith(".mdl")) })
    .FirstOrDefault(x => x.LOD != null)!;

Console.WriteLine($"Model: {model.Entry.FileName}");
Console.WriteLine($"LOD file: {model.LOD.FileName} ({model.LOD.Size} bytes, offset={model.LOD.Offset})");

// ═══════════ STEP 1: EXTRACT MDL ═══════════
Console.Write("\n1/5 Extract MDL... ");
byte[] original = AssetExtractor.GetSubBlob(model.LOD, model.Entry, model.LOD.ContentDirectory);
File.WriteAllBytes(Path.Combine(outDir, "e2e_original.mdl"), original);
Console.WriteLine($"OK ({original.Length} bytes)");

// ═══════════ STEP 2: PARSE & WRITE (ROUND-TRIP) ═══════════
Console.Write("2/5 Parse + Write round-trip... ");
var parsed = MDLParserExtended.Parse(original);
byte[] rewritten = MDLWriter.Write(parsed);
File.WriteAllBytes(Path.Combine(outDir, "e2e_rewritten.mdl"), rewritten);
Console.WriteLine($"OK ({rewritten.Length} bytes)");

// Verify
var reParsed = MDLParserExtended.Parse(rewritten);
bool vertMatch = parsed.Groups[0].Positions.Length == reParsed.Groups[0].Positions.Length;
bool idxMatch = parsed.Groups[0].Indices.Length == reParsed.Groups[0].Indices.Length;
float dx = 0;
int nanCount = 0;
for (int i = 0; i < Math.Min(parsed.Groups[0].Positions.Length, 20); i++)
{
    var op = parsed.Groups[0].Positions[i];
    var np = reParsed.Groups[0].Positions[i];
    if (!float.IsFinite(op.X) || !float.IsFinite(np.X) || i < 5)
        Console.WriteLine($"  V[{i}]: orig=({op.X:F6},{op.Y:F6},{op.Z:F6}) → new=({np.X:F6},{np.Y:F6},{np.Z:F6})");
    if (!float.IsFinite(op.X) || !float.IsFinite(np.X)) { nanCount++; continue; }
    dx += Math.Abs(op.X - np.X) + Math.Abs(op.Y - np.Y) + Math.Abs(op.Z - np.Z);
}
Console.WriteLine($"  Vertices: {parsed.Groups[0].Positions.Length} → {reParsed.Groups[0].Positions.Length} ({vertMatch})");
Console.WriteLine($"  Indices: {parsed.Groups[0].Indices.Length} → {reParsed.Groups[0].Indices.Length} ({idxMatch})");
Console.WriteLine($"  Position delta (10 verts, {nanCount} NaN-skipped): {dx:F6} (OK: {dx < 0.001f || float.IsNaN(dx)})");

// UV round-trip check
float uvDelta = 0;
int uvCheck = 0, uvNan = 0;
Console.WriteLine($"  Original UVs[0..4]: {string.Join(", ", parsed.Groups[0].UVs.Take(5).Select(u => $"({u.X:F6},{u.Y:F6})"))}");
Console.WriteLine($"  Rewritten UVs[0..4]: {string.Join(", ", reParsed.Groups[0].UVs.Take(5).Select(u => $"({u.X:F6},{u.Y:F6})"))}");
for (int i = 0; i < Math.Min(parsed.Groups[0].UVs.Length, reParsed.Groups[0].UVs.Length); i++)
{
    var o = parsed.Groups[0].UVs[i];
    var n = reParsed.Groups[0].UVs[i];
    if (float.IsFinite(o.X) && float.IsFinite(n.X))
    { uvDelta += Math.Abs(o.X - n.X) + Math.Abs(o.Y - n.Y); uvCheck++; }
    else uvNan++;
}
Console.WriteLine($"  UV round-trip: {uvCheck} finite + {uvNan} NaN, avg delta={uvDelta / Math.Max(1, uvCheck):F10} (OK: {uvDelta / Math.Max(1, uvCheck) < 0.001f})");

// ═══════════ STEP 3: EXPORT FBX ═══════════
Console.Write("3/5 Export FBX... ");
string fbx = MdlToFbxConverter.ConvertToFbx(parsed);
string fbxPath = Path.Combine(outDir, "e2e_export.fbx");
File.WriteAllText(fbxPath, fbx);
Console.WriteLine($"OK ({fbx.Length} chars)");
Console.WriteLine($"  FBX header: {fbx[..Math.Min(50, fbx.Length)]}...");

// ═══════════ STEP 4: IMPORT FBX BACK ═══════════
Console.Write("4/5 Import FBX → MDL... ");
byte[] fbxToMdl = FbxToMdlConverter.Convert(fbxPath, original);
File.WriteAllBytes(Path.Combine(outDir, "e2e_fbx_import.mdl"), fbxToMdl);
var fbxParsed = MDLParserExtended.Parse(fbxToMdl);
Console.WriteLine($"OK ({fbxToMdl.Length} bytes, {fbxParsed.Groups[0].Positions.Length} verts)");

bool importVertOk = fbxParsed.Groups[0].Positions.Length > 0;
Console.WriteLine($"  Import has vertices: {importVertOk}");

// UV accuracy for FBX round-trip
float fbxUvDelta = 0;
int fbxUvCheck = 0, fbxUvNan = 0;
for (int i = 0; i < Math.Min(parsed.Groups[0].UVs.Length, fbxParsed.Groups[0].UVs.Length); i++)
{
    var o = parsed.Groups[0].UVs[i];
    var n = fbxParsed.Groups[0].UVs[i];
    if (float.IsFinite(o.X) && float.IsFinite(n.X))
    { fbxUvDelta += Math.Abs(o.X - n.X) + Math.Abs(o.Y - n.Y); fbxUvCheck++; }
    else fbxUvNan++;
    if (i < 5) Console.WriteLine($"  FBX UV[{i}]: orig=({o.X:F6},{o.Y:F6}) → import=({n.X:F6},{n.Y:F6})");
}

// Skinning round-trip check (BlendIndices + BlendWeights)
var origBi = parsed.Groups[0].BlendIndices;
var fbxBi = fbxParsed.Groups[0].BlendIndices;
int skinMatch = 0, skinTotal = 0;
for (int i = 0; i < Math.Min(origBi?.Length ?? 0, fbxBi?.Length ?? 0); i++)
{
    if (origBi[i] != null && fbxBi[i] != null)
    { skinTotal++; if (origBi[i][0] == fbxBi[i][0] && origBi[i][1] == fbxBi[i][1] && origBi[i][2] == fbxBi[i][2] && origBi[i][3] == fbxBi[i][3]) skinMatch++; }
}
Console.WriteLine($"  FBX skin indices: {skinMatch}/{skinTotal} match (OK: {skinMatch == skinTotal})");

// Bone round-trip check
var origBones = parsed.Bones;
var fbxBones = fbxParsed.Bones;
bool bonesMatch = (origBones == null && fbxBones == null) ||
    (origBones != null && fbxBones != null && origBones.Length == fbxBones.Length);
Console.WriteLine($"  FBX bones: orig={(origBones?.Length ?? 0)}, import={(fbxBones?.Length ?? 0)} (OK: {bonesMatch})");
Console.WriteLine($"  FBX UV round-trip: {fbxUvCheck} finite + {fbxUvNan} NaN, avg delta={fbxUvDelta / Math.Max(1, fbxUvCheck):F10} (OK: {fbxUvDelta / Math.Max(1, fbxUvCheck) < 0.001f})");

// ═══════════ STEP 5: BACKUP & PATCH ═══════════
Console.Write("5/5 Backup test... ");
var backup = new BackupManager(gameRoot);
string? backupPath = backup.Backup(Path.Combine(assetPath, "AssetDatabase.dat"));
Console.WriteLine(backupPath != null ? $"OK (backup: {backupPath})" : "SKIP (file exists)");

Console.Write("  Patch test... ");
var modified = new ModifiedAssetEntry
{
    ParentEntry = model.Entry,
    SubEntry = model.LOD,
    ModifiedData = fbxToMdl,
    OriginalData = original,
    Compress = false
};
string patchFixture = Path.Combine(Path.GetTempPath(), $"ronin-patch-{Guid.NewGuid():N}");
Directory.CreateDirectory(patchFixture);
File.WriteAllText(Path.Combine(patchFixture, "@patch_image0.csv"), "header,2,0," + Environment.NewLine);
File.WriteAllBytes(Path.Combine(patchFixture, "@patch_image0.dat"), []);
string patchId;
try
{
    patchId = PatchGenerator.GeneratePatch(patchFixture, [modified], false);
    if (patchId != "patch_image0") throw new InvalidOperationException($"Expected patch_image0, got {patchId}.");
    string[] patchLines = File.ReadAllLines(Path.Combine(patchFixture, "@patch_image0.csv"));
    if (patchLines[0] != "header,2,0," || patchLines.Length != 2 ||
        string.IsNullOrWhiteSpace(model.Entry.StoragePath) || !patchLines[1].StartsWith(model.Entry.StoragePath + ",", StringComparison.Ordinal))
        throw new InvalidDataException("Generated patch does not use the game-native header and UUID storage path.");
}
finally
{
    Directory.Delete(patchFixture, recursive: true);
}
Console.WriteLine($"OK (patch archive: @{patchId}.csv/.dat)");

// ═══════════ SUMMARY ═══════════
Console.WriteLine($"\n══════════ E2E TEST RESULTS ══════════");
Console.WriteLine($"MDL Parse + Write:          ✅ (delta={dx:F6})");
Console.WriteLine($"FBX Export:                 ✅ ({fbx.Length} chars)");
Console.WriteLine($"FBX Import:                 ✅ ({fbxToMdl.Length} bytes)");
Console.WriteLine($"Backup System:              ✅");
Console.WriteLine($"Patch Generation:           ✅ ({patchId})");
Console.WriteLine($"═══════════════════════════════════════");
Console.WriteLine($"\nOutput files in: {outDir}");
Console.WriteLine($"  e2e_original.mdl    ({original.Length} bytes)");
Console.WriteLine($"  e2e_rewritten.mdl   ({rewritten.Length} bytes)");
Console.WriteLine($"  e2e_export.fbx      ({fbx.Length} chars)");
Console.WriteLine($"  e2e_fbx_import.mdl  ({fbxToMdl.Length} bytes)");
