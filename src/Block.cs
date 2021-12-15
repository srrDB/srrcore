using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace srrcore
{
    public class Block
    {
        public SrrBlockHeader SrrBlockHeader;

        public long BlockPosition;

        public Block(SrrBlockHeader srrBlockHeader, long startOffset, ref BinaryReader reader)
        {
            this.BlockPosition = startOffset;
            this.SrrBlockHeader = srrBlockHeader;
        }
    }

    public class SrrHeaderBlock : Block
    {
        public string AppName { get; set; }

        //public SrrHeaderBlock(byte[] blockBytes, long filePos) : base(blockBytes, filePos)
        public SrrHeaderBlock(SrrBlockHeader srrBlockHeader, long startOffset, ref BinaryReader reader) : base(srrBlockHeader, startOffset, ref reader)
        {
            this.AppName = new string(reader.ReadChars(reader.ReadUInt16()));

            //ushort fileNameLength = BitConverter.ToUInt16(blockBytes, 7);
            //byte[] fileName = new byte[fileNameLength];
            //Buffer.BlockCopy(blockBytes, 9, fileName, 0, fileNameLength);
            //this.AppName = Encoding.UTF8.GetString(fileName);
        }
    }

    public class SrrStoredFileBlock : Block
    {
        public string FileName { get; set; }

        public long FileOffset { get; set; }

        public uint FileLength { get; set; }

        public static byte[] GetHeader(string fileName, int fileSize)
        {
            string hex = "6A6A6A0080";
            byte[] header = Enumerable.Range(0, hex.Length).Where(x => x % 2 == 0).Select(x => Convert.ToByte(hex.Substring(x, 2), 16)).ToArray();

            byte[] addSize = BitConverter.GetBytes((UInt32)(fileSize));
            byte[] pathLength = BitConverter.GetBytes((ushort)fileName.Length);
            byte[] headerSize = BitConverter.GetBytes((ushort)(5 + 2 + 4 + 2 + fileName.Length));

            byte[] fileNameBytes = Encoding.ASCII.GetBytes(fileName);

            byte[] newHeader = header.Concat(headerSize).Concat(addSize).Concat(pathLength).Concat(fileNameBytes).ToArray();

            return newHeader;
        }

        public SrrStoredFileBlock(SrrBlockHeader srrBlockHeader, long startOffset, ref BinaryReader reader) : base(srrBlockHeader, startOffset, ref reader)
        {
            this.FileName = new string(reader.ReadChars(reader.ReadUInt16()));
            this.FileOffset = reader.BaseStream.Position;
            this.FileLength = this.SrrBlockHeader.AddSize;
        }
    }

    public class SrrRarFileBlock : Block
    {
        public string FileName { get; set; }

        public uint FileLength { get; set; } = 0;

        public SrrRarFileBlock(SrrBlockHeader srrBlockHeader, long startOffset, ref BinaryReader reader) : base(srrBlockHeader, startOffset, ref reader)
        {
            this.FileName = new string(reader.ReadChars(reader.ReadUInt16()));
        }
    }

    public class RarVolumeHeaderBlock : Block
    {
        public RarVolumeHeaderBlock(SrrBlockHeader srrBlockHeader, long startOffset, ref BinaryReader reader) : base(srrBlockHeader, startOffset, ref reader)
        {
        }
    }

    public class RarPackedFileBlock : Block
    {
        public byte CompressionMethod { get; set; }

        public ulong PackedSize { get; set; }

        public ulong UnpackedSize { get; set; }

        public uint FileCrc { get; set; }

        public string FileName { get; set; }

        public RarPackedFileBlock(SrrBlockHeader srrBlockHeader, long startOffset, ref BinaryReader reader) : base(srrBlockHeader, startOffset, ref reader)
        {
            //this.PackedSize = reader.ReadUInt32(); //already read when reading "addsize"
            this.PackedSize = this.SrrBlockHeader.AddSize;

            this.UnpackedSize = reader.ReadUInt32();
            reader.ReadByte(); //os (skip)
            this.FileCrc = reader.ReadUInt32();
            reader.ReadUInt32(); //datetime (skip)
            reader.ReadByte(); //unpackVersion (skip)
            this.CompressionMethod = reader.ReadByte();
            ushort nameLength = reader.ReadUInt16();
            reader.ReadUInt32(); //file attributes (skip)

            // if large file flag is set, next are 4 bytes each for high order bits of file sizes
            if ((this.SrrBlockHeader.Flags & 0x100) != 0)
            {
                //read additional bytes
                this.PackedSize += reader.ReadUInt32() * 0x100000000ul;
                this.UnpackedSize += reader.ReadUInt32() * 0x100000000ul;
            }

            this.FileName = new string(reader.ReadChars(nameLength));

            // the file name can be null-terminated, especially in the case of utf-encoded ones. cut it off if necessary
            if (this.FileName.IndexOf('\0') >= 0)
            {
                this.FileName = this.FileName.Substring(0, this.FileName.IndexOf('\0'));
            }
        }
    }

    public class RarRecoveryBlock : RarPackedFileBlock
    {
        public uint RecoverySectors { get; protected set; }

        public ulong DataSectors { get; protected set; }

        public RarRecoveryBlock(SrrBlockHeader srrBlockHeader, long startOffset, ref BinaryReader reader) : base(srrBlockHeader, startOffset, ref reader)
        {
            this.FileName = new string(reader.ReadChars(8)); //'Protect+', overwrites 'RR'
            this.RecoverySectors = reader.ReadUInt32();
            this.DataSectors = reader.ReadUInt64();
        }
    }

    public class RarOldRecoveryBlock : Block
    {
        public uint PackedSize { get; protected set; }

        public ushort RecoverySectors { get; protected set; }

        public uint DataSectors { get; protected set; }

        public RarOldRecoveryBlock(SrrBlockHeader srrBlockHeader, long startOffset, ref BinaryReader reader) : base(srrBlockHeader, startOffset, ref reader)
        {
            this.PackedSize = reader.ReadUInt32();

            reader.BaseStream.Seek(1, SeekOrigin.Current); //rar version

            this.RecoverySectors = reader.ReadUInt16();
            this.DataSectors = reader.ReadUInt32();
        }
    }
}
