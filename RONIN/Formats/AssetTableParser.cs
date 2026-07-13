using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YakumoLib;

namespace RONIN.Formats
{
    public struct AssetTableEntry
    {
        public string TableName { get; }
        public uint u_4;
        public uint u_8;
        public uint nameOffset;
        public uint u_10;
        public uint dataOffset;
        public UUID data;

        public string UUIDString => data.GetString();

        public AssetTableEntry(BinaryReader reader)
        {
            long pos = reader.BaseStream.Position;
            
            uint recSize = reader.ReadUInt32();
            u_4 = reader.ReadUInt32();
            u_8 = reader.ReadUInt32();
            nameOffset = reader.ReadUInt32();
            u_10 = reader.ReadUInt32();
            dataOffset = reader.ReadUInt32();

            reader.BaseStream.Seek(pos + nameOffset, 0);
            uint nameLength = reader.ReadUInt32();

            TableName = Encoding.UTF8.GetString(reader.ReadBytes((int)nameLength - 1));

            reader.BaseStream.Seek(pos + dataOffset, 0);
            data = new UUID {  
                a = reader.ReadUInt32(), b = reader.ReadUInt32(), c = reader.ReadUInt32(), d = reader.ReadUInt32(),
            };


        }


    }

    public static class AssetTableParser
    {
        public static AssetTableEntry[] GetAssetTableEntries(byte[] data)
        {
            BinaryReader reader = new BinaryReader(new MemoryStream(data));
            reader.BaseStream.Seek(0x10, 0);

            uint offsetBase = reader.ReadUInt32();
            uint assetCount = reader.ReadUInt32();
            uint unk_18 = reader.ReadUInt32();

            uint[] entryOffsets = new uint[assetCount];

            for (uint i = 0; i < assetCount; i++)
            {
                entryOffsets[i] = offsetBase+4+reader.ReadUInt32();
            }

            AssetTableEntry[] output = new AssetTableEntry[assetCount];
            for (uint i = 0; i < assetCount; i++)
            {
                reader.BaseStream.Seek(entryOffsets[i], 0);
                output[i] = new AssetTableEntry(reader);

            }




            return output;
        }
    }
}
