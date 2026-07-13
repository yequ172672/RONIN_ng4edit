using System.Buffers.Binary;
using System.IO.Enumeration;
using System.Runtime.InteropServices;
using System.Text;
using YakumoLib.Assets;

namespace YakumoLib.Database
{
    public sealed class AssetDatabaseReader
    {
        private const uint DBMagic = 1111770444;

        public static IReadOnlyList<AssetEntry> Read(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            return Read(bytes);
        }

        public static IReadOnlyList<AssetEntry> Read(ReadOnlySpan<byte> data)
        {
            AssetDatabaseHeader header = MemoryMarshal.Read<AssetDatabaseHeader>(data);

            if (header.Magic != DBMagic)
            {
                throw new InvalidDataException("Bad database magic!");
            }

            var entries = new List<AssetEntry>((int)header.FileCount);

            uint group1Offset = 0x14;
            uint languageIDOffset = group1Offset + (header.Group1Count * 0x10);
            uint fileEntryOffset = languageIDOffset + (header.LanguageInfoCount * 12);
            uint stringHeaderOffset = fileEntryOffset + (header.FileCount * 40);
            uint stringDataOffset = stringHeaderOffset + 52;

            var stringTable = data[(int)stringDataOffset..];


            for (int i = 0; i < header.FileCount; i++)
            {
                long offset = fileEntryOffset + (i * 40);
                var entrySpan = data.Slice((int)offset, 40);

                entries.Add(ReadEntry(entrySpan, stringTable));

            }



            return entries;
        }

        private static AssetEntry ReadEntry(ReadOnlySpan<byte> entry, ReadOnlySpan<byte> stringTable)
        {
            AssetDatabaseFileEntry asset = MemoryMarshal.Read<AssetDatabaseFileEntry>(entry);

            return new AssetEntry
            {
                AssetID = new UUID { a = asset.IdA, b = asset.IdB, c = asset.IdC, d = asset.IdD },
                Path = ReadNullTerminatedString(stringTable, (int)asset.PathOffset),
                Type = AssetTypeParser.GetType(ReadNullTerminatedString(stringTable, (int)asset.TypeOffset))
            };



        }
        private static string ReadNullTerminatedString(ReadOnlySpan<byte> buffer, int offset)
        {
            var slice = buffer[offset..];
            int nullIndex = slice.IndexOf((byte)0);

            if (nullIndex < 0)
                throw new InvalidDataException($"Unterminated string at offset {offset}");

            return Encoding.UTF8.GetString(slice[..nullIndex]);
        }


    }
}
