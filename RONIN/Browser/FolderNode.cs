using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YakumoLib.Assets;

namespace RONIN.Browser
{
    public sealed class FolderNode
    {
        public string Name { get; }
        public string FullPath { get; }
        public Dictionary<string, FolderNode> Subfolders { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<AssetEntry> Assets { get; } = new();

        public FolderNode(string name, string fullPath) { Name = name; FullPath = fullPath; }

        public static FolderNode BuildTree(IEnumerable<AssetEntry> entries)
        {
            var root = new FolderNode("AssetDatabase.dat", "");

            foreach (var entry in entries)
            {
                var segments = entry.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                var current = root;

                for (int i = 0; i < segments.Length - 1; i++)
                {
                    var segment = segments[i];
                    if (!current.Subfolders.TryGetValue(segment, out var child))
                    {
                        var childPath = current.FullPath.Length == 0 ? segment : $"{current.FullPath}/{segment}";
                        child = new FolderNode(segment, childPath);
                        current.Subfolders[segment] = child;
                    }
                    current = child;
                }

                current.Assets.Add(entry);
            }

            return root;
        }
    }
}
