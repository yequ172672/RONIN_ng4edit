using YakumoLib.Formats;
using RONIN.Formats;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using YakumoLib.Assets;

namespace RONIN.Preview
{
    public sealed class TexturePreviewProvider : IAssetPreviewProvider
    {
        public bool CanPreview(AssetEntry entry) => entry.Type == AssetType.Texture;

        public async Task<object?> LoadPreviewAsync(AssetEntry entry)
        {
            return await Task.Run(() =>
            {
                try
                {
                    string archiveRootDirectory = entry.ContentDirectory
                        ?? entry.SubEntries?.FirstOrDefault()?.ContentDirectory
                        ?? throw new InvalidDataException($"Texture '{entry.Path}' has no content directory.");

                    var texture = TexturePackageDds.Extract(entry, archiveRootDirectory);
                    var (color, width, height) = DDSTexture.Load(texture.DdsBytes);

                    return (object?)new TexturePreviewViewModel(
                        BitmapHelper.CreateFromData(color, width, height),
                        width,
                        height,
                        texture.MipCount);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to decode texture '{entry.Path}': {ex.Message}", ex);
                }
            });
        }
    }

    public sealed record TexturePreviewViewModel(BitmapSource Image, int Width, int Height, int MipCount);
}
