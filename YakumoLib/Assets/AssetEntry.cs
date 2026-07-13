using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace YakumoLib.Assets
{
    public sealed record AssetEntry
    {
        public required string Path { get; init; }
        public required AssetType Type { get; init; }
        public required UUID AssetID { get; init; }
        public long? Size { get; init; }
        public long? CompressedSize { get; init; }
        public long? Offset { get; init; }
        public string? SourceArchive { get; init; }

        public bool? Pruned { get; init; }
        public string? ContentDirectory { get; init; }


        public string FileName => System.IO.Path.GetFileName(Path);
        public string Extension => System.IO.Path.GetExtension(Path);
        public string StringAssetID => AssetID.GetString();
        public string SizeFormatted => FormatSize(Size);

        public string CmpSizeFormatted => FormatSize(CompressedSize);

        public static string FormatSize(long? bytes)
        {
            if (bytes == null) return "";

            string[] units = { "B", "KB", "MB", "GB" };

            double size = (double)bytes;
            int unit = 0;

            while (size >= 1024 && unit < units.Length - 1)
            {
                size /= 1024;
                unit++;
            }

            return $"{size:0.##} {units[unit]}";

        }


        public IReadOnlyList<SubAssetEntry>? SubEntries { get; init; }
    }
}
