using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace YakumoLib.Assets
{
    public interface IAssetParser<T>
    {
        AssetType SupportedType { get; }

        bool CanParse(AssetEntry entry);

        T Parse(ReadOnlySpan<byte> data, AssetEntry entry);
    }
}
