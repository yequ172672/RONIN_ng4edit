using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YakumoLib.Database;

namespace YakumoLib.Assets
{
    public sealed class AssetLibrary
    {
        private readonly Dictionary<UUID, AssetEntry> _byId;
        private readonly Dictionary<string, AssetEntry> _byPath;

        public IReadOnlyCollection<AssetEntry> All => _byId.Values;
        public int Count => _byId.Count;

        private AssetLibrary(Dictionary<UUID, AssetEntry> byId, Dictionary<string, AssetEntry> byPath)
        {
            _byId = byId;
            _byPath = byPath;
        }

        public AssetEntry? GetByUUID(UUID id)
        {
            if (_byId.ContainsKey(id))
            {
                return _byId[id];
            }
            else
            {
                return null;
            }

            
        }


        public static AssetLibrary Load(
            string assetPath,
            IProgress<string>? progress = null)

            
        {
            progress?.Report("Starting AssetDB Read...");
            var entries = AssetDatabaseReader.Read(Path.Join(assetPath, "AssetDatabase.dat"));

            var byId = new Dictionary<UUID, AssetEntry>(entries.Count);
            foreach (var entry in entries)
                byId[entry.AssetID] = entry;


            progress?.Report($"Building file entries...");

            var csvProgress = new Progress<(string fileName, int done, int total, int size)>(p =>
            {
                if (p.done >= 0)
                    Console.WriteLine($"{p.fileName} ({p.size/ 1e+6f:F2}MB) [DONE] ({p.done}/{p.total})");
            });

            var csvReader = new CsvReader();
            csvReader.LoadAssetInfo(byId, assetPath, csvProgress);

            progress?.Report("Building paths...");
            var byPath = new Dictionary<string, AssetEntry>(byId.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var entry in byId.Values)
                byPath[entry.Path] = entry;


            progress?.Report("Done!");
            return new AssetLibrary(byId, byPath);
        }



    }
}
