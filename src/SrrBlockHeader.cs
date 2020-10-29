namespace srrcore
{
    public struct SrrBlockHeader
    {
        public ushort HeaderCrc;

        public RarBlockType BlockType;

        public ushort Flags;

        public ushort HeaderSize;

        public uint AddSize;

        public uint FullSize => this.AddSize + this.HeaderSize;
    }

    public struct RarPackedFileHeader
    {
        public ulong unpackedSize;

        public byte os;

        public uint fileCrc;

        public ulong fileTime;

        public byte unpackVersion;

        public byte compressionMethod;

        public ushort nameLength;

        public ulong fileAttributes;
    }
}
