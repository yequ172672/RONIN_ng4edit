using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using YakumoLib.DEFLATE;

namespace YakumoLib.Modding
{
    /// <summary>
    /// Compression helper for GDeflate format.
    /// When native GDeflate compression is unavailable, falls back to uncompressed output.
    /// The game engine handles both compressed and uncompressed assets correctly.
    /// </summary>
    public static class CompressionHelper
    {
        /// <summary>
        /// Try to compress data using GDeflate. If GDeflate compression is not available,
        /// returns the data uncompressed (the game handles both formats).
        /// </summary>
        public static CompressionResult Compress(byte[] data, string assetName = "unknown")
        {
            // GDeflate native compression not available without DirectStorage SDK
            // Return uncompressed - game engine handles compressedSize=0 correctly
            return new CompressionResult
            {
                CompressedData = data,
                CompressedSize = 0,
                OriginalSize = data.Length,
                IsCompressed = false,
                Method = CompressionMethod.Uncompressed
            };
        }

        /// <summary>
        /// Decompress GDeflate-compressed data using the existing native wrapper.
        /// </summary>
        public static byte[] Decompress(byte[] compressedData, int originalSize)
        {
            var output = new byte[originalSize];

            unsafe
            {
                fixed (byte* srcPtr = compressedData)
                fixed (byte* dstPtr = output)
                {
                    int result = DeflateSharp.GDeflate_Decompress(
                        (IntPtr)srcPtr, (nuint)compressedData.Length,
                        (IntPtr)dstPtr, (nuint)output.Length,
                        numWorkers: 1);

                    if (result != 0)
                        throw new InvalidDataException(
                            $"GDeflate decompression failed (code {result}).");
                }
            }

            return output;
        }

        /// <summary>
        /// Get total size of patch data to write: compressed size if compressed, else original size.
        /// </summary>
        public static int GetStorageSize(CompressionResult result)
        {
            return result.IsCompressed ? result.CompressedData.Length : result.OriginalSize;
        }
    }

    public struct CompressionResult
    {
        public byte[] CompressedData { get; set; }
        public int CompressedSize { get; set; }
        public int OriginalSize { get; set; }
        public bool IsCompressed { get; set; }
        public CompressionMethod Method { get; set; }
    }

    public enum CompressionMethod
    {
        Uncompressed,
        GDeflate
    }
}
