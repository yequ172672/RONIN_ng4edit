using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YakumoLib.Assets;

namespace YakumoLib.Database
{
    public sealed record CsvAssetLine
    {
        public required string Path { get; init; }
        public required long Offset { get; init; }
        public required long Size { get; init; }
        public required long CompressedSize { get; init; }
        public required int FileCount { get; init; }
        public required string SourceArchive { get; init; }
        public required string Unknown { get; init; }
        public required string Metadata { get; init; }
        public required IReadOnlyList<SubAssetEntry> SubEntries { get; init; }

    }
}
