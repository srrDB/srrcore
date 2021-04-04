using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.CompilerServices;
using Force.Crc32;

namespace srrcore
{
    public class Srr
    {
        public const int HEADERLENGTH = 7;

        private string _fileName; //including path

        //public string ApplicationName;

        public string ApplicationName
        {
            get
            {
                //TODO: handle multiple srrheaderblocks
                return this.BlockList.Where(x => x.SrrBlockHeader.BlockType == RarBlockType.SrrHeader).Select(x => x as SrrHeaderBlock).Select(x => x.AppName).FirstOrDefault();
            }
        }

        //public bool Compressed;

        public bool Compressed
        {
            get
            {
                return this.BlockList.Where(x => x.SrrBlockHeader.BlockType == RarBlockType.RarPackedFile).Select(x => x as RarPackedFileBlock).Any(x => x.CompressionMethod != 0x30);
            }
        }

        public long FileSize;

        //public uint FileCRC;

        public List<Block> BlockList = new List<Block>();

        //public List<SrrFileInfo> StoredFileInfos = new List<SrrFileInfo>();

        public List<SrrFileInfo> StoredFileInfos
        {
            get
            {
                return this.BlockList.Where(x => x.SrrBlockHeader.BlockType == RarBlockType.SrrStoredFile).Select(x => x as SrrStoredFileBlock).Select(x => new SrrFileInfo
                {
                    FileName = x.FileName,
                    FileOffset = x.FileOffset,
                    FileSize = x.FileLength
                }).ToList();
            }
        }

        //public List<SrrFileInfo> RaredFileInfos = new List<SrrFileInfo>();

        public List<SrrFileInfo> RaredFileInfos
        {
            get
            {
                return this.BlockList.Where(x => x.SrrBlockHeader.BlockType == RarBlockType.SrrRarFile).Select(x => x as SrrRarFileBlock).Select(x => new SrrFileInfo
                {
                    FileName = x.FileName,
                    FileSize = x.FileLength,
                    FileCrc = this.SfvData.FirstOrDefault(y => y.FileName == x.FileName)?.FileCrc ?? 0
                }).ToList();
            }
        }

        //public List<SrrFileInfo> ArchivedFileInfos = new List<SrrFileInfo>();

        public List<SrrFileInfo> ArchivedFileInfos
        {
            get
            {
                return this.BlockList.Where(x => x.SrrBlockHeader.BlockType == RarBlockType.RarPackedFile).Select(x => x as RarPackedFileBlock).GroupBy(x => x.FileName).Select(x => new SrrFileInfo
                {
                    FileName = x.LastOrDefault().FileName,
                    FileSize = x.LastOrDefault().UnpackedSize,
                    FileCrc = x.LastOrDefault().FileCrc,
                }).ToList();
            }
        }

        public SrrFileInfo RarRecovery
        {
            get
            {
                if (this.BlockList.Any(x => x.SrrBlockHeader.BlockType == RarBlockType.RarOldRecovery))
                {
                    //old recovery
                    RarOldRecoveryBlock rorb = this.BlockList.FirstOrDefault(x => x.SrrBlockHeader.BlockType == RarBlockType.RarOldRecovery) as RarOldRecoveryBlock;

                    if (rorb != null)
                    {
                        return new SrrFileInfo
                        {
                            FileName = "Protect!",
                            FileSize = 0
                        };
                    }
                }
                else if (this.BlockList.Any(x => x.SrrBlockHeader.BlockType == RarBlockType.RarNewSub))
                {
                    RarRecoveryBlock rrb = this.BlockList.FirstOrDefault(x => x.SrrBlockHeader.BlockType == RarBlockType.RarNewSub) as RarRecoveryBlock;

                    if (rrb != null)
                    {
                        return new SrrFileInfo
                        {
                            FileName = "Protect+",
                            FileSize = 0
                        };
                    }
                }

                return null;
            }
        }

        public List<SrrFileInfo> SfvData = new List<SrrFileInfo>();

        public bool HasNfo => this.StoredFileInfos.Select(x => x.FileName.ToLower()).Any(x => x.EndsWith(".nfo"));
        public bool HasSrs => this.StoredFileInfos.Select(x => x.FileName.ToLower()).Any(x => x.EndsWith(".srs"));

        private bool ProcessSfv(string fileData)
        {
            string[] sfvLines = fileData
                .Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .ToArray();

            foreach (string sfvLine in sfvLines)
            {
                if (sfvLine[0] == ';' || sfvLine.Length < 10)
                {
                    //comment, ignore for now
                }
                else
                {
                    //sfv line
                    int spaceIndex = sfvLine.LastIndexOf(' ');

                    if (spaceIndex > -1)
                    {
                        string fileName = sfvLine.Substring(0, spaceIndex);

                        //if name is surrounded by quotations
                        if (fileName.StartsWith("\"") && fileName.EndsWith("\""))
                        {
                            fileName = fileName.Substring(1, fileName.Length - 1);
                        }

                        string crc = sfvLine.Substring(spaceIndex + 1, 8);

                        this.SfvData.Add(new SrrFileInfo
                        {
                            FileName = fileName,
                            FileCrc = Convert.ToUInt32(crc, 16)
                        });
                    }
                }
            }

            return true;
        }

        private BinaryReader _reader;

        public Srr(string filename)
        {
            this._fileName = filename;

            if (!System.IO.File.Exists(this._fileName))
            {
                //if file does not exists
                throw new Exception(this._fileName + " doesn't exists");
            }

            this._reader = new BinaryReader(File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.Read));
            ReadSrr_New(this._reader);
        }

        public Srr(Stream stream)
        {
            this._reader = new BinaryReader(stream);
            ReadSrr_New(this._reader);
        }

        //TODO: prevent duplicate filename
        //TODO: prevent filename starting with bad character
        //TODO: prevent filename containing bad character
        //TODO: don't use this._fileName (see GetStoredFileData, it's only set if it's a file, not set if it's a stream);
        public void AddStoredFile(string fileName, byte[] fileData)
        {
            string hex = "6A6A6A0080";
            byte[] header = Enumerable.Range(0, hex.Length).Where(x => x % 2 == 0).Select(x => Convert.ToByte(hex.Substring(x, 2), 16)).ToArray();

            byte[] addSize = BitConverter.GetBytes((UInt32)(fileData.Length));
            byte[] pathLength = BitConverter.GetBytes((ushort)fileName.Length);
            byte[] headerSize = BitConverter.GetBytes((ushort)(5 + 2 + 4 + 2 + fileName.Length));

            byte[] fileNameBytes = Encoding.ASCII.GetBytes(fileName);

            //TODO: there should be a more efficient way to concat byte arrays
            byte[] newHeader = header.Concat(headerSize).Concat(addSize).Concat(pathLength).Concat(fileNameBytes).ToArray();

            Block blockToAddAfter = this.BlockList.Where(x => x.SrrBlockHeader.BlockType == RarBlockType.SrrStoredFile).LastOrDefault();

            byte[] srrBytes = File.ReadAllBytes(this._fileName);

            byte[] first = srrBytes.Take((int)blockToAddAfter.BlockPosition + (int)blockToAddAfter.SrrBlockHeader.FullSize).ToArray();
            byte[] after = srrBytes.Skip((int)blockToAddAfter.BlockPosition + (int)blockToAddAfter.SrrBlockHeader.FullSize).ToArray();

            int totalFilesize = first.Length + after.Length + newHeader.Length + fileData.Length;

            File.WriteAllBytes(this._fileName, first.Concat(newHeader).Concat(fileData).Concat(after).ToArray());
        }

        public void RenameFile(string oldFilename, string newFilename)
        {
            //TODO
        }

        public void ReorderFiles(string[] orderedFilenames)
        {
            //TODO
        }

        //TODO: don't use this._fileName (see GetStoredFileData, it's only set if it's a file, not set if it's a stream);
        public void RemoveStoredFile(string fileName)
        {
            foreach (SrrStoredFileBlock srrStoredFileBlock in this.BlockList.Where(x => x.SrrBlockHeader.BlockType == RarBlockType.SrrStoredFile).Select(x => x as SrrStoredFileBlock))
            {
                if (srrStoredFileBlock.FileName == fileName)
                {
                    byte[] srrBytes = File.ReadAllBytes(this._fileName);
                    int remaning = srrBytes.Length - (int)srrStoredFileBlock.FileLength;

                    byte[] first = srrBytes.Take((int)srrStoredFileBlock.BlockPosition).ToArray();
                    byte[] after = srrBytes.Skip((int)srrStoredFileBlock.BlockPosition).Skip((int)srrStoredFileBlock.SrrBlockHeader.FullSize).Take(remaning).ToArray();

                    File.WriteAllBytes(this._fileName, first.Concat(after).ToArray());
                }
            }
        }

        public byte[] GetStoredFileData(string fileName)
        {
            SrrFileInfo srrFileInfo = this.StoredFileInfos.FirstOrDefault(x => x.FileName == fileName);

            if (srrFileInfo != null)
            {
                this._reader.BaseStream.Seek((int)srrFileInfo.FileOffset, SeekOrigin.Begin);

                return this._reader.ReadBytes((int)srrFileInfo.FileSize);
            }

            return null;
        }

        private void ReadSrr_New(BinaryReader reader)
        {
            this.FileSize = reader.BaseStream.Length;

            SrrRarFileBlock currentRarFile = null;

            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                long startOffset = reader.BaseStream.Position;

                byte[] header = reader.ReadBytes(HEADERLENGTH);

                SrrBlockHeader srrBlockHeader = new SrrBlockHeader
                {
                    HeaderCrc = BitConverter.ToUInt16(header, 0),
                    BlockType = Enum.IsDefined(typeof(RarBlockType), header[2]) ? (RarBlockType)header[2] : RarBlockType.Unknown,
                    Flags = BitConverter.ToUInt16(header, 3),
                    HeaderSize = BitConverter.ToUInt16(header, 5)
                };

                int addSizeFlag = srrBlockHeader.Flags & 0x8000;

                if (addSizeFlag > 0 || srrBlockHeader.BlockType == RarBlockType.RarPackedFile || srrBlockHeader.BlockType == RarBlockType.RarNewSub)
                {
                    srrBlockHeader.AddSize = reader.ReadUInt32();
                }

                //calculate rar file size
                if (currentRarFile != null && srrBlockHeader.BlockType != RarBlockType.SrrRarFile)
                {
                    currentRarFile.FileLength += srrBlockHeader.FullSize;
                }

                switch (srrBlockHeader.BlockType)
                {
                    case RarBlockType.SrrHeader:
                        this.BlockList.Add(new SrrHeaderBlock(srrBlockHeader, startOffset, ref reader));
                        break;
                    case RarBlockType.SrrStoredFile:
                        SrrStoredFileBlock srrStoredFileBlock = new SrrStoredFileBlock(srrBlockHeader, startOffset, ref reader);
                        this.BlockList.Add(srrStoredFileBlock);

                        reader.ReadBytes((int)srrBlockHeader.AddSize); //skip stored file data
                        break;
                    case RarBlockType.SrrRarFile:
                        currentRarFile = new SrrRarFileBlock(srrBlockHeader, startOffset, ref reader);
                        this.BlockList.Add(currentRarFile);
                        break;
                    case RarBlockType.RarVolumeHeader:
                        this.BlockList.Add(new RarVolumeHeaderBlock(srrBlockHeader, startOffset, ref reader));
                        reader.ReadBytes((int)srrBlockHeader.HeaderSize - HEADERLENGTH); //this skip block?
                        break;
                    case RarBlockType.RarPackedFile:
                        RarPackedFileBlock rarPackedFileBlock = new RarPackedFileBlock(srrBlockHeader, startOffset, ref reader);

                        this.BlockList.Add(rarPackedFileBlock);

                        reader.BaseStream.Seek(startOffset + rarPackedFileBlock.SrrBlockHeader.HeaderSize, SeekOrigin.Begin);
                        break;
                    case RarBlockType.RarOldRecovery:
                        this.BlockList.Add(new RarOldRecoveryBlock(srrBlockHeader, startOffset, ref reader));
                        break;
                    case RarBlockType.RarNewSub:
                        //if (isRecovery)
                        if (true)
                        {
                            RarRecoveryBlock rarRecoveryBlock = new RarRecoveryBlock(srrBlockHeader, startOffset, ref reader); //BROKEN 2020-11-22 04:18:00

                            this.BlockList.Add(rarRecoveryBlock);

                            reader.BaseStream.Seek(startOffset + rarRecoveryBlock.SrrBlockHeader.HeaderSize, SeekOrigin.Begin);
                        }
                        else
                        {
                            this.BlockList.Add(new Block(srrBlockHeader, startOffset, ref reader));
                        }
                        break;
                    case RarBlockType.Unknown: //TODO: fix
                        break;
                    case RarBlockType.SrrOsoHash: //wont implement
                    case RarBlockType.RarMin:
                    case RarBlockType.RarMax:
                    case RarBlockType.OldComment:
                    case RarBlockType.OldAuthenticity1:
                    case RarBlockType.OldSubblock:
                    case RarBlockType.OldAuthenticity2:
                        //case RarBlockType.SrrRarPadding:
                        reader.ReadBytes(srrBlockHeader.HeaderSize - 7); //skip block
                        break;
                    default:
                        //new Block(srrBlockHeader, startOffset, ref reader);
                        reader.BaseStream.Seek(startOffset + srrBlockHeader.FullSize, SeekOrigin.Begin);
                        break;
                }
            }

            //sfv processing
            foreach (SrrFileInfo sfvFile in this.StoredFileInfos.Where(x => x.FileName.ToLower().EndsWith(".sfv")))
            {
                byte[] sfvData = this.GetStoredFileData(sfvFile.FileName);
                this.ProcessSfv(Encoding.UTF8.GetString(sfvData));
            }
        }

        /*public static Srr ReadSrr(BinaryReader reader)
        {
            Srr srr = new Srr();

            SrrFileInfo rarFileInfo = null;

            srr.FileSize = reader.BaseStream.Length;
            //srr.FileCRC = Crc32Algorithm.Compute(reader.ReadBytes((int) reader.BaseStream.Length).ToArray());

            reader.BaseStream.Seek(0, SeekOrigin.Begin);

            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                long startOffset = reader.BaseStream.Position;

                byte[] headerBuff = new byte[HEADERLENGTH];
                reader.Read(headerBuff, 0, HEADERLENGTH); //read into buffer

                //create header
                SrrBlockHeader header = new SrrBlockHeader
                {
                    HeaderCrc = BitConverter.ToUInt16(headerBuff, 0),
                    BlockType = Enum.IsDefined(typeof(RarBlockType), headerBuff[2]) ? (RarBlockType) headerBuff[2] : RarBlockType.Unknown,
                    Flags = BitConverter.ToUInt16(headerBuff, 3),
                    HeaderSize = BitConverter.ToUInt16(headerBuff, 5),
                    AddSize = 0
                };

                int addSizeFlag = header.Flags & 0x8000;

                if (addSizeFlag > 0 || header.BlockType == RarBlockType.RarPackedFile || header.BlockType == RarBlockType.RarNewSub)
                {
                    header.AddSize = reader.ReadUInt32();
                }

                long offset = reader.BaseStream.Position;
                reader.BaseStream.Seek(startOffset + 2, SeekOrigin.Begin);

                byte[] crcData = reader.ReadBytes(header.HeaderSize - 2);
                uint crc = Crc32Algorithm.Compute(crcData.Select(c => (byte)c).ToArray()) & 0xffff;

                //move back seek, now we know addsize and crc
                reader.BaseStream.Seek(offset, SeekOrigin.Begin);

                switch (header.BlockType)
                {
                    case RarBlockType.SrrHeader:
                        srr.ApplicationName = new string(reader.ReadChars(reader.ReadUInt16()));
                        break;
                    case RarBlockType.SrrStoredFile:
                        string fileName = new string(reader.ReadChars(reader.ReadUInt16()));
                        ushort fileOffset = (ushort) reader.BaseStream.Position;

                        byte[] fileData = new byte[header.AddSize]; //allocate byte array
                        reader.Read(fileData, 0, (int) header.AddSize); //read data to byte array

                        srr.StoredFileInfos.Add(new SrrFileInfo
                        {
                            FileName = fileName,
                            FileSize = header.AddSize,
                            FileData = fileData,
                            FileCrc = Crc32Algorithm.Compute(fileData)
                        });

                        if (fileName.EndsWith(".sfv"))
                        {
                            //sfv file
                            srr.SfvData.AddRange(ProcessSfv(System.Text.Encoding.Default.GetString(fileData)));
                        }

                        break;
                    case RarBlockType.SrrRarFile:
                        string fileName2 = new string(reader.ReadChars(reader.ReadUInt16()));

                        rarFileInfo = new SrrFileInfo
                        {
                            FileName = fileName2,
                            FileSize = 0
                        };

                        srr.RaredFileInfos.Add(rarFileInfo);

                        break;
                    case RarBlockType.RarPackedFile:
                        ulong packedSize = header.AddSize;

                        ulong unpackedSize = reader.ReadUInt32(); //us
                        reader.ReadByte(); //os (skip)
                        uint fileCrc = reader.ReadUInt32(); //crc
                        reader.ReadUInt32(); //datetime (skip)
                        reader.ReadByte(); //unpackVersion (skip)
                        byte compressionMethod = reader.ReadByte();
                        ushort nameLength = reader.ReadUInt16();
                        reader.ReadUInt32(); //file attributes (skip)

                        // if large file flag is set, next are 4 bytes each for high order bits of file sizes
                        if ((header.Flags & 0x100) != 0)
                        {
                            //read additional bytes
                            packedSize += reader.ReadUInt32() * 0x100000000ul;
                            unpackedSize += reader.ReadUInt32() * 0x100000000ul;
                        }

                        // and finally, the file name.
                        string fileName3 = new string(reader.ReadChars(nameLength));

                        // the file name can be null-terminated, especially in the case of utf-encoded ones.  cut it off if necessary
                        if (fileName3.IndexOf('\0') >= 0)
                        {
                            fileName3 = fileName3.Substring(0, fileName3.IndexOf('\0'));
                        }

                        srr.Compressed = compressionMethod != 0x30;

                        SrrFileInfo archiveInfo = srr.ArchivedFileInfos.FirstOrDefault(x => x.FileName == fileName3);

                        if (archiveInfo != null)
                        {
                            archiveInfo.FileCrc = fileCrc;
                        }
                        else
                        {
                            srr.ArchivedFileInfos.Add(new SrrFileInfo
                            {
                                FileName = fileName3,
                                FileSize = unpackedSize,
                                FileCrc = fileCrc
                            });
                        }

                        //skip addional header (skipHeader)
                        reader.BaseStream.Seek(startOffset + header.HeaderSize, SeekOrigin.Begin);
                        break;
                    case RarBlockType.RarNewSub: //TODO: fix much better
                        ulong packedSize2 = header.AddSize;

                        ulong unpackedSize2 = reader.ReadUInt32(); //us
                        reader.ReadByte(); //os (skip)
                        uint fileCrc2 = reader.ReadUInt32(); //crc
                        reader.ReadUInt32(); //datetime (skip)
                        reader.ReadByte(); //unpackVersion (skip)
                        byte compressionMethod2 = reader.ReadByte();
                        ushort nameLength2 = reader.ReadUInt16();
                        reader.ReadUInt32(); //file attributes (skip)

                        // if large file flag is set, next are 4 bytes each for high order bits of file sizes
                        if ((header.Flags & 0x100) != 0)
                        {
                            //read additional bytes
                            packedSize2 += reader.ReadUInt32() * 0x100000000ul;
                            unpackedSize2 += reader.ReadUInt32() * 0x100000000ul;
                        }

                        // and finally, the file name.
                        string fileName4 = new string(reader.ReadChars(nameLength2));
                       
                        if(fileName4 == "RR")
                        {
                            string recovery = "Protect+";
                        }

                        //skip addional header (skipHeader)
                        reader.BaseStream.Seek(startOffset + header.HeaderSize, SeekOrigin.Begin);

                        break;
                    default:
                        //don't read more after we've reached the end
                        if (startOffset + header.HeaderSize + header.AddSize <= reader.BaseStream.Length)
                        {
                            reader.BaseStream.Seek(startOffset + header.HeaderSize + header.AddSize, SeekOrigin.Begin);
                        }

                        break;
                }

                if (rarFileInfo != null && header.BlockType != RarBlockType.SrrRarFile)
                {
                    rarFileInfo.FileSize += header.FullSize;
                }
            }

            //sfv mapping
            if(srr.RaredFileInfos.Count > 0)
            {
                srr.RaredFileInfos.Select(x =>
                {
                    x.FileCrc = srr.SfvData.FirstOrDefault(y => y.FileName.ToLower() == x.FileName.ToLower()).FileCrc;
                    return x;
                }).ToList();
            }

            return srr;
        }*/
    }
}