using System.Diagnostics;
using YakumoLib.Assets;
using YakumoLib.Database;

var stopwatch = Stopwatch.StartNew();

var progress = new Progress<string>(msg => Console.WriteLine(msg));

var library = AssetLibrary.Load(
    assetPath: "F:\\SteamLibrary\\steamapps\\common\\NINJAGAIDEN4\\Assets\\",
    progress: progress);

stopwatch.Stop();

Console.WriteLine($"Full load took: {stopwatch.ElapsedMilliseconds}ms ({stopwatch.ElapsedMilliseconds/1000:F2}s) for {library.Count} assets");


Console.ReadLine();