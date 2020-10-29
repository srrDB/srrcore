namespace srrcore
{
    public enum RarBlockType : byte
    {
        Unknown = 0,
        RarVolumeHeader = 0x73,
        RarPackedFile = 0x74,
        RarOldRecovery = 0x78,
        RarNewSub = 0x7A,

        //not intresting in web (only for reconstruction)
        RarMin = 0x72, //"RAR Marker"
        RarMax = 0x7B, //"Archive end"
        OldComment = 0x75,
        OldAuthenticity1 = 0x76,
        OldSubblock = 0x77,
        OldAuthenticity2 = 0x79,

        //srr
        SrrHeader = 0x69,
        SrrStoredFile = 0x6A,
        SrrRarFile = 0x71,

        //new
        SrrOsoHash = 0x6B,
        SrrRarPadding = 0x6C
    }
}
