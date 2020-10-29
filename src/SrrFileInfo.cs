namespace srrcore
{
    public class SrrFileInfo
    {
        public string FileName;

        public ulong FileSize;

        public byte[] FileData;

        public uint FileCrc; //from sfv, matched using filename
    }
}
