using YakumoLib.Assets;
using YakumoLib.Modding;

internal static class Ng4ModRealWorkflowTests
{
    private const string ModelPath = "Assets/Character/PL/PL0000/Model/DDX1/PL0000_DDX1";

    public static void Run()
    {
        string? assetsDirectory = Environment.GetEnvironmentVariable("RONIN_NG4_ASSETS");
        if (string.IsNullOrWhiteSpace(assetsDirectory) || !Directory.Exists(assetsDirectory))
        {
            Console.WriteLine("Real NG4 workflow skipped; set RONIN_NG4_ASSETS to a game Assets directory to enable it.");
            return;
        }

        string root = Path.Combine(Path.GetTempPath(), "ronin-ng4mod-real-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            AssetLibrary library = AssetLibrary.Load(assetsDirectory);
            AssetEntry model = library.All.Single(entry => entry.Path.Equals(ModelPath, StringComparison.OrdinalIgnoreCase));
            Ng4ModWorkspaceExportResult workspace = Ng4ModWorkspaceService.Export(model, library.All, root);
            string packagePath = Path.Combine(root, "real-smoke.ng4mod");
            Ng4ModWorkspacePackageResult package = Ng4ModWorkspaceService.Package(
                workspace.GlbPath,
                library,
                new Ng4ModWorkspacePackageRequest(
                    "ronin.real.smoke",
                    "RONIN real workflow smoke",
                    "0.0.0",
                    "RONIN",
                    "Local verification package",
                    [],
                    packagePath));

            if (!File.Exists(package.PackagePath) || new FileInfo(package.PackagePath).Length == 0)
                throw new InvalidDataException("Real workflow did not create an NG4MOD package.");
            Ng4ModPackageExporter.VerifyPackage(package.PackagePath);
            Console.WriteLine($"Real NG4 workflow passed: {package.PackagePath}");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
