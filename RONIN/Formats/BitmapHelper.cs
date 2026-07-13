using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RONIN.Formats
{
    public static class BitmapHelper
    {
        public static BitmapSource CreateFromData(byte[] rgba, int width, int height)
        {
            var bitmap = BitmapSource.Create(
                width, height,
                96, 96,
                PixelFormats.Bgra32,
                null,
                SwapRedBlue(rgba),
                width * 4);

            bitmap.Freeze();
            return bitmap;
        }

        private static byte[] SwapRedBlue(byte[] rgba)
        {
            var bgra = new byte[rgba.Length];
            for (int i = 0; i < rgba.Length; i += 4)
            {
                bgra[i + 0] = rgba[i + 2];
                bgra[i + 1] = rgba[i + 1];
                bgra[i + 2] = rgba[i + 0];
                bgra[i + 3] = rgba[i + 3];
            }
            return bgra;
        }


    }
}
