using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YakumoLib.Assets;

namespace RONIN.Preview
{
    public interface IAssetPreviewProvider
    {
        bool CanPreview(AssetEntry entry);
        Task<object?> LoadPreviewAsync(AssetEntry entry);
    }
}
