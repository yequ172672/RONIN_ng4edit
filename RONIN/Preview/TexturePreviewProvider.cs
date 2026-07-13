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
    public sealed class TexturePreviewProvider : IAssetPreviewProvider // might need to not make this Sealed so i can extend it for the other Texture types
    {
        public bool CanPreview(AssetEntry entry) => entry.Type == AssetType.Texture;

        public async Task<object?> LoadPreviewAsync(AssetEntry entry)
        {
            if (entry.SubEntries is null || entry.SubEntries.Count == 0)
                return null;

            var imageHeaderEntry = entry.SubEntries.FirstOrDefault(s => s.FileName.Equals("Image.img"));

            if (imageHeaderEntry is null)
                return null;

            return await Task.Run(() =>
            {
                try
                {
                    byte[] decompressed = AssetExtractor.GetSubBlob(imageHeaderEntry, entry, imageHeaderEntry.ContentDirectory);
                    var br = new BinaryReader(new MemoryStream(decompressed));
                    uint totalSize = br.ReadUInt32();
                    uint mipMapCount = br.ReadUInt32();

                    br.ReadBytes((int)mipMapCount*8); // we don't really need the info here.

                    SubAssetEntry[] mipLevels = new SubAssetEntry[mipMapCount];

                    for (uint i = 0; i < mipMapCount; i++)
                    {
                        mipLevels[i] = entry.SubEntries.FirstOrDefault(s => s.FileName.Equals($"mip{i}.img")) ?? throw new InvalidDataException("Missing mip level!");
                    }

                    byte[] finalTx = new byte[totalSize];
                    finalTx = br.ReadBytes(148); // DDS Header

                    for (uint i = 0; i < mipMapCount; i++)
                    {
                        finalTx = finalTx.Concat(AssetExtractor.GetSubBlob(mipLevels[i], entry, imageHeaderEntry.ContentDirectory)).ToArray();
                    }

                    var (color, width, height) = DDSTexture.Load(finalTx);


                   
                    return (object?)new TexturePreviewViewModel(BitmapHelper.CreateFromData(color, width, height), width, height);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Failed to decode texture '{entry.Path}': {ex.Message}", ex);
                }


            });

        }

    }
    public sealed record TexturePreviewViewModel(BitmapSource Image, int Width, int Height);
}
