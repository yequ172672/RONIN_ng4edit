using RONIN.Formats;
using System;
using System.Collections.Generic;
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
    public sealed class AnimationPreviewProvider : IAssetPreviewProvider // might need to not make this Sealed so i can extend it for the other Texture types
    {
        public bool CanPreview(AssetEntry entry) => entry.Type == AssetType.AnimationTexture;

        public async Task<object?> LoadPreviewAsync(AssetEntry entry)
        {
            if (entry.SubEntries is null || entry.SubEntries.Count == 0)
                return null;

            var ddsData = entry.SubEntries.FirstOrDefault(s => s.FileName.Equals("Image.img"));

            if (ddsData is null)
                return null;

            return await Task.Run(() =>
            {
                try
                {
                    byte[] decompressed = AssetExtractor.GetSubBlob(ddsData, entry, ddsData.ContentDirectory);

                    var (color, width, height) = DDSTexture.Load(decompressed);



                    return (object?)new AnimationPreviewViewModel(BitmapHelper.CreateFromData(color, width, height), width, height);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to decode anim texture '{entry.Path}': {ex.Message}", ex);
                }


            });

        }

    }
    public sealed record AnimationPreviewViewModel(BitmapSource Image, int Width, int Height);
}

