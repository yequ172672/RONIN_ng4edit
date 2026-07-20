using System.IO.Compression;
using RoninNg4Host;

internal static class HostTextureArchiveTests
{
    public static void Run()
    {
        string sourceDirectory = Path.Combine(Path.GetTempPath(), $"ronin-host-test-source-{Guid.NewGuid():N}");
        Directory.CreateDirectory(sourceDirectory);
        File.WriteAllText(Path.Combine(sourceDirectory, "texture.png"), "texture-data");
        string archivePath;
        try
        {
            using (TemporaryTextureArchive archive = TemporaryTextureArchive.CreateFromDirectory(sourceDirectory))
            {
                archivePath = archive.Path;
                Assert(File.Exists(archivePath), "Temporary texture ZIP was not created.");
                Assert(Path.GetDirectoryName(archivePath) == Path.TrimEndingDirectorySeparator(Path.GetTempPath()),
                    "Temporary texture ZIP was not created in the system temp directory.");
                using ZipArchive zip = ZipFile.OpenRead(archivePath);
                ZipArchiveEntry entry = zip.GetEntry("texture.png")
                    ?? throw new InvalidOperationException("Temporary texture ZIP is missing texture.png.");
                using var reader = new StreamReader(entry.Open());
                Assert(reader.ReadToEnd() == "texture-data", "Temporary texture ZIP entry content is invalid.");
            }
            Assert(!File.Exists(archivePath), "Disposing the temporary texture ZIP did not delete it.");
        }
        finally
        {
            Directory.Delete(sourceDirectory, recursive: true);
        }
        Console.WriteLine("Host texture archive tests passed.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
