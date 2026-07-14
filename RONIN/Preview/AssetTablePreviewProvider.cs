using HelixToolkit.Wpf.SharpDX;
using RONIN.Formats;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using YakumoLib.Assets;

namespace RONIN.Preview
{
    public sealed class AssetTablePreviewProvider : IAssetPreviewProvider 
    {
        public bool CanPreview(AssetEntry entry) => entry.Type == AssetType.AssetTable;

        public async Task<object?> LoadPreviewAsync(AssetEntry entry)
        {
            if (entry.SubEntries is null || entry.SubEntries.Count == 0)
                return null;

            var assetTable = entry.SubEntries.FirstOrDefault(s => s.FileName.Equals("AssetTable.bin"));

            if (assetTable is null)
                return null;

            return await Task.Run(() =>
            {
                try
                {
                    AssetTableEntry[] entries = AssetTableParser.GetAssetTableEntries(AssetExtractor.GetSubBlob(assetTable, entry, assetTable.ContentDirectory));



                    return (object?)new AssetTablePreviewViewModel { Entries = new ObservableCollection<AssetTableEntry>(entries) };
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to read asset table '{entry.Path}': {ex.Message}", ex);
                }


            });

        }

    }
    public sealed record AssetTablePreviewViewModel
    {
        public required ObservableCollection<AssetTableEntry> Entries { get; init; }
    }
}
