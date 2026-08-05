using System.Diagnostics;

var tests = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase)
{
    ["--mdl-gltf-tests"] = MdlGltfTests.Run,
    ["--model-texture-set-tests"] = ModelTextureSetTests.Run,
    ["--ng4-lod-param-tests"] = Ng4LodParamTests.Run,
    ["--ng4mod-package-tests"] = Ng4ModPackageTests.Run,
    ["--ng4mod-exporter-tests"] = Ng4ModExporterTests.Run,
    ["--ng4mod-workspace-tests"] = Ng4ModWorkspaceTests.Run,
    ["--ng4mod-real-workflow-tests"] = Ng4ModRealWorkflowTests.Run,
    ["--parent-payload-tests"] = ParentPayloadBuilderTests.Run
};

if (args.Length == 0 || args.Any(argument => argument is "--help" or "-h"))
{
    PrintUsage();
    return;
}

foreach (string argument in args)
{
    if (!tests.TryGetValue(argument, out Action? test))
    {
        Console.Error.WriteLine($"Unknown test switch: {argument}");
        PrintUsage();
        Environment.ExitCode = 2;
        return;
    }

    Stopwatch stopwatch = Stopwatch.StartNew();
    try
    {
        test();
        Console.WriteLine($"{argument} passed in {stopwatch.Elapsed.TotalSeconds:F2}s.");
    }
    catch (Exception exception)
    {
        Console.Error.WriteLine($"{argument} failed: {exception}");
        Environment.ExitCode = 1;
        return;
    }
}

static void PrintUsage()
{
    Console.WriteLine("RONIN prototype verification");
    Console.WriteLine("Usage: dotnet run --project RONIN.Test -c Release -- <switch> [...]");
    Console.WriteLine("  --mdl-gltf-tests             MDL <-> GLB round-trip (requires local fixture)");
    Console.WriteLine("  --model-texture-set-tests    PNG/TGA, DDS and mip workflow");
    Console.WriteLine("  --ng4-lod-param-tests        LOD threshold and replacement plan");
    Console.WriteLine("  --ng4mod-package-tests       ng4mod v2 manifest contract");
    Console.WriteLine("  --ng4mod-exporter-tests      ng4mod v2 payload/hash verification");
    Console.WriteLine("  --ng4mod-workspace-tests     workspace path and preflight checks");
    Console.WriteLine("  --ng4mod-real-workflow-tests real Assets -> GLB/TGA -> NG4MOD smoke");
    Console.WriteLine("  --parent-payload-tests       parent payload assembly");
}
