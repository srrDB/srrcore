namespace srrcore
{
    public class SrrFileInfo
    {
        public string FileName;

        public ulong FileSize;

        public long FileOffset;

        //public byte[] FileData;

        public uint FileCrc; //from sfv, matched using filename
    }
}
