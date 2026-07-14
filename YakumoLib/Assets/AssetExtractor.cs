using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using YakumoLib.DEFLATE;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace YakumoLib.Assets
{



    public static class AssetExtractor
    {

        private static byte[] ReadExact(FileStream fs, int size)
        {
            var buffer = new byte[size];
            int totalRead = 0;
            while (totalRead < size)
            {
                int read = fs.Read(buffer, totalRead, size - totalRead);
                totalRead += read;
            }
            return buffer;
        }

        public static byte[] GetParentBlob(AssetEntry parent, string archiveRootDirectory)
        {
            if (parent.Offset is null || parent.SourceArchive is null)
            {
                throw new InvalidOperationException($"Asset {parent.Path} has been pruned.");
            }

            string archivePath = Path.Combine(archiveRootDirectory, parent.SourceArchive+".dat");

            using var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            fs.Seek(parent.Offset.Value, SeekOrigin.Begin);

            bool compressed = parent.CompressedSize != 0;
            int readSize = compressed ? (int)parent.CompressedSize!.Value : (int)parent.Size!.Value;

            var raw = ReadExact(fs, readSize);

            if (!compressed)
                return raw;

            var output = new byte[parent.Size!.Value];

            unsafe
            {
                fixed (byte* srcPtr = raw)
                fixed (byte* dstPtr = output)
                {
                    int result = DeflateSharp.GDeflate_Decompress(
                        (IntPtr)srcPtr, (nuint)raw.Length,
                        (IntPtr)dstPtr, (nuint)output.Length,
                        numWorkers: 1);

                    if (result != 0)
                        throw new InvalidDataException(
                            $"GDeflate decompression failed for '{parent.Path}' (code {result}).");
                }
            }

            return output;

        }

        public static byte[] GetSubBlob(SubAssetEntry sub, AssetEntry parent, string archiveRootDirectory)
        {
            if (sub.SourceArchive is null)
            {
                throw new InvalidOperationException($"Asset {sub.FileName} has been pruned.");
            }

            if (!sub.IsGlobal)
            {
                byte[] parentData = GetParentBlob(parent, archiveRootDirectory);
                if (parentData == null)
                {
                    return [];
                }

                // TODO: Seek into the byte data and return a range from sub.Offset to sub.Size (compresed size does not apply to in-parent sub archives)


                return parentData.AsSpan((int)sub.Offset, (int)sub.Size).ToArray();
            }
            else
            {
                string archivePath = Path.Combine(archiveRootDirectory, sub.SourceArchive + ".dat");

                using var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                fs.Seek(sub.Offset, SeekOrigin.Begin);

                bool compressed = sub.CompressedSize != 0;
                int readSize = compressed ? (int)sub.CompressedSize : (int)sub.Size;

                var raw = ReadExact(fs, readSize);

                if (!compressed)
                    return raw;

                var output = new byte[sub.Size];

                unsafe
                {
                    fixed (byte* srcPtr = raw)
                    fixed (byte* dstPtr = output)
                    {
                        int result = DeflateSharp.GDeflate_Decompress(
                            (IntPtr)srcPtr, (nuint)raw.Length,
                            (IntPtr)dstPtr, (nuint)output.Length,
                            numWorkers: 1);

                        if (result != 0)
                            throw new InvalidDataException(
                                $"GDeflate decompression failed for '{parent.Path}' (code {result}).");
                    }
                }

                return output;
            }





        }

        public static void ExtractAll(AssetEntry target, string outputDir)
        {

            foreach (SubAssetEntry sub in target.SubEntries)
            {
                byte[] data = GetSubBlob(sub, target, sub.ContentDirectory);

                Directory.CreateDirectory(Path.Join(outputDir, Path.GetFileNameWithoutExtension(target.FileName)));
                File.WriteAllBytes(Path.Join(outputDir, Path.GetFileNameWithoutExtension(target.FileName), sub.FileName), data);
            }


        }



        /*public static byte[] ReadBytes(AssetEntry parent, SubAssetEntry sub, string archiveRootDirectory)
        {
            string archivePath = Path.Combine(archiveRootDirectory, sub.SourceArchive);

            using var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);


            bool global = sub.IsGlobal;
            bool compressed = false;

            if (!global)
            {
                compressed = (parent.CompressedSize != 0);
            }
            else
            {
                compressed = (sub.CompressedSize != 0);
            }

            // if it's not global we need to decompress the entire parent blob, then seek to the sub.offset
            // if it is global we just need to decompress the blob the sub points too. 
            // this format is super weird


        }*/

    }
}
