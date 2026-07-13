using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace YakumoLib.Assets
{
    public sealed record SubAssetEntry
    {
        public required string FileName { get; init; }
        public required long Size { get; init; }
        public required long CompressedSize { get; init;  }
        public required long Offset { get; init; }
        public required string SourceArchive { get; init; }
        public required string ContentDirectory { get; init; }

        public required bool IsCompressed { get; init; }
        public required bool IsGlobal { get; init; }



        public string Extension => System.IO.Path.GetExtension(FileName);
    }
}
