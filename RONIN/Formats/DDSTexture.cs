using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RONIN.Formats
{
    public static class DDSTexture
    {
        public static (byte[] Color, int Width, int Height) Load(byte[] ddsBytes)
        {
            using var image = Pfim.Pfimage.FromStream(new MemoryStream(ddsBytes));

            int bytesPerPixel = image.Format switch
            {
                Pfim.ImageFormat.Rgba32 => 4,
                Pfim.ImageFormat.Rgb24 => 3,
                Pfim.ImageFormat.Rgb8 => 1,
                _ => throw new NotSupportedException($"Unhandled Pfim image format: {image.Format}")
            };

            byte[] data = image.Data;
            int rowBytes = image.Width * bytesPerPixel;



            byte[] rgba = bytesPerPixel switch
            {
                4 => SwapRedBlueInPlace(data),
                1 => ExpandGreyToRgba(data, image.Width, image.Height),
                3 => (ExpandRgbToRgba(data, image.Width, image.Height)), // TODO: Fix normal map color space
                _ => throw new NotSupportedException()
            };

            return (rgba, image.Width, image.Height);
        }

        private static byte[] ExpandGreyToRgba(byte[] grey, int width, int height)
        {
            var rgba = new byte[width * height * 4];
            for (int i = 0; i < grey.Length; i++)
            {
                rgba[i * 4 + 0] = 255;
                rgba[i * 4 + 1] = 255;
                rgba[i * 4 + 2] = 255;
                rgba[i * 4 + 3] = grey[i];
            }
            return rgba;
        }



        private static byte[] SwapRedBlueInPlace(byte[] data)
        {
            for (int i = 0; i < data.Length; i += 4)
            {
                (data[i], data[i + 2]) = (data[i + 2], data[i]);
            }
            return data;
        }

        private static byte[] ExpandRgbToRgba(byte[] rgb, int width, int height)
        {
            var rgba = new byte[width * height * 4];
            for (int i = 0; i < width * height; i++)
            {
                rgba[i * 4 + 0] = rgb[i * 3 + 0];
                rgba[i * 4 + 1] = rgb[i * 3 + 1];
                rgba[i * 4 + 2] = rgb[i * 3 + 2];
                rgba[i * 4 + 3] = 255;
            }
            return rgba;
        }

        private static byte[] RemoveStridePadding(byte[] src, int width, int height, int stride)
        {
            var dst = new byte[width * height * 4];
            int rowBytes = width * 4;
            for (int y = 0; y < height; y++)
                Buffer.BlockCopy(src, y * stride, dst, y * rowBytes, rowBytes);
            return dst;
        }


    }
}
