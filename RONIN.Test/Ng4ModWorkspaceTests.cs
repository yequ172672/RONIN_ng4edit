using YakumoLib.Modding;
using YakumoLib.Assets;
using System.Runtime.CompilerServices;

internal static class Ng4ModWorkspaceTests
{
    public static void Run()
    {
        MapsCanonicalLogicalPathUnderExportRoot();
        RejectsUnsafeLogicalPath();
        RejectsEmptyBatch();
        RejectsDuplicateWorkspaceModelsBeforeConversion();
        Console.WriteLine("NG4MOD workspace tests passed.");
    }

    private static void MapsCanonicalLogicalPathUnderExportRoot()
    {
        string root = Path.Combine(Path.GetTempPath(), "ronin-workspace-test");
        string actual = Ng4ModWorkspaceService.MapWorkspacePath(
            root,
            "Assets/Character/PL/PL0000/Model/DDX1/PL0000_DDX1");
        string expected = Path.Combine(root, "Assets", "Character", "PL", "PL0000", "Model", "DDX1", "PL0000_DDX1");
        Assert(actual == Path.GetFullPath(expected), "Logical model path was not mapped under the export root.");
    }

    private static void RejectsUnsafeLogicalPath()
    {
        AssertThrows<InvalidDataException>(() =>
            Ng4ModWorkspaceService.MapWorkspacePath(Path.GetTempPath(), "Assets/Character/../../escape"));
    }

    private static void RejectsEmptyBatch()
    {
        AssetLibrary library = (AssetLibrary)RuntimeHelpers.GetUninitializedObject(typeof(AssetLibrary));
        AssertThrows<ArgumentException>(() => Ng4ModWorkspaceService.PackageMany([], library,
            new Ng4ModWorkspacePackageRequest("test", "Test", "1.0.0", "", "", [], "out.ng4mod")));
    }

    private static void RejectsDuplicateWorkspaceModelsBeforeConversion()
    {
        string root = Path.Combine(Path.GetTempPath(), "ronin-workspace-duplicate-" + Guid.NewGuid().ToString("N"));
        try
        {
            string[] glbs = ["first.glb", "second.glb"];
            foreach (string name in glbs)
            {
                string workspace = Path.Combine(root, Path.GetFileNameWithoutExtension(name));
                Directory.CreateDirectory(Path.Combine(workspace, "textures"));
                File.WriteAllBytes(Path.Combine(workspace, name), "glTF"u8.ToArray());
                File.WriteAllText(Path.Combine(workspace, "textures", ModelTextureSetService.ManifestFileName),
                    "{\"modelAssetId\":\"same-model\"}");
            }

            AssetLibrary library = (AssetLibrary)RuntimeHelpers.GetUninitializedObject(typeof(AssetLibrary));
            AssertThrows<InvalidDataException>(() => Ng4ModWorkspaceService.PackageMany(
                glbs.Select(name => new Ng4ModWorkspaceModelInput(Path.Combine(root, Path.GetFileNameWithoutExtension(name), name))),
                library,
                new Ng4ModWorkspacePackageRequest("test", "Test", "1.0.0", "", "", [], Path.Combine(root, "out.ng4mod"))));
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static void AssertThrows<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
