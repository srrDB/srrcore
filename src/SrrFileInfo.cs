namespace srrcore
{
    public class SrrFileInfo
    {
        public string FileName;

        public ulong FileSize;

        public long FileOffset;

        //public byte[] FileData;

        public uint FileCrc; //if stored calculated, or from sfv matched using filename
    }
}
