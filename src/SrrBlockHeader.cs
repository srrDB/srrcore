namespace SrrCore
{
    struct SrrBlockHeader
    {
        public ushort HeaderCrc;

        public RarBlockType BlockType;

        public ushort Flags;

        public ushort HeaderSize;

        public uint AddSize;

        public uint FullSize => this.AddSize + this.HeaderSize;
    }
}
