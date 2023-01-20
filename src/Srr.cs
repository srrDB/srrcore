using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Force.Crc32;

namespace srrcore
{
    public class Srr
    {
        private const int HEADERLENGTH = 7;

        private BinaryReader _reader;

        private MemoryStream _srrStream = new MemoryStream();

        private string _fileName; //including path, not always specified

        private List<Block> BlockList = new List<Block>();

        public List<string> Warnings = new List<string>();

        public long SrrSize;

        public string ApplicationName
        {
            get
            {
                List<SrrHeaderBlock> headerBlocks = this.BlockList.Where(x => x.SrrBlockHeader.BlockType == RarBlockType.SrrHeader).Select(x => x as SrrHeaderBlock).ToList();

                if (headerBlocks.Count > 1)
                {
                    this.Warnings.Add("Multiple header blocks found");
                }

                if (headerBlocks.Any())
                {
                    return headerBlocks.FirstOrDefault().AppName;
                }

                return "";
            }
        }

        public bool Compressed
        {
            get
            {
                List<RarPackedFileBlock> packedFileBlocks = this.BlockList.Where(x => x.SrrBlockHeader.BlockType == RarBlockType.RarPackedFile).Select(x => x as RarPackedFileBlock).ToList();

                return packedFileBlocks.Any(x => x.CompressionMethod != 0x30);
            }
        }

        public List<SrrFileInfo> StoredFileInfos
        {
            get
            {
                return this.BlockList.Where(x => x.SrrBlockHeader.BlockType == RarBlockType.SrrStoredFile).Select(x => x as SrrStoredFileBlock).Select(x => new SrrFileInfo
                {
                    FileName = x.FileName,
                    FileOffset = x.FileOffset,
                    FileSize = x.FileLength,
                    FileCrc = x.FileCrc
                }).ToList();
            }
        }

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

        public Srr(string filename, bool calculateStoredCrc = false)
        {
            this._fileName = filename;

            if (!File.Exists(this._fileName))
            {
                //if file does not exists
                throw new FileNotFoundException(this._fileName + " doesn't exists");
            }

            //copy file to a memorystream that will be used instead of the file
            using (FileStream fileStream = File.Open(this._fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                this._srrStream = new MemoryStream();
                this._srrStream.SetLength(fileStream.Length);

                //fileStream.Read(this._srrStream.GetBuffer(), 0, (int)fileStream.Length);
                fileStream.CopyTo(this._srrStream);
            }

            using (this._reader = new BinaryReader(this._srrStream))
            {
                ReadSrr(this._reader, calculateStoredCrc);
            }
        }

        public Srr(Stream stream, bool calculateStoredCrc = false)
        {
            stream.Position = 0; //we must reset position before copy

            this._srrStream = new MemoryStream();
            this._srrStream.SetLength(stream.Length);
            stream.CopyTo(this._srrStream);

            using (this._reader = new BinaryReader(this._srrStream))
            {
                ReadSrr(this._reader, calculateStoredCrc);
            }
        }

        public void AddStoredFile(string fileName, byte[] fileData)
        {
            if (this.StoredFileInfos.Any(x => x.FileName.ToLower() == fileName.ToLower()))
            {
                //file with the same name already exists
                return;
            }

            if (fileName.StartsWith("/") || fileName.StartsWith("\\"))
            {
                //starts with bad character
                return;
            }

            //\/:*?"<>|
            if (new[] { ":", "*", "?", "\"", "<", ">", "|" }.Any(c => fileName.Contains(c)))
            {
                //filename contains invalid charcaters
                return;
            }

            byte[] newHeader = SrrStoredFileBlock.GetHeader(fileName, fileData.Length);

            //add the file after the last file
            Block blockToAddAfter = this.BlockList.Where(x => x.SrrBlockHeader.BlockType == RarBlockType.SrrStoredFile).LastOrDefault();

            byte[] srrBytes = this._srrStream.ToArray();

            byte[] first = srrBytes.Take((int)blockToAddAfter.BlockPosition + (int)blockToAddAfter.SrrBlockHeader.FullSize).ToArray();
            byte[] after = srrBytes.Skip((int)blockToAddAfter.BlockPosition + (int)blockToAddAfter.SrrBlockHeader.FullSize).ToArray();

            //calculate total size
            int totalFilesize = first.Length + after.Length + newHeader.Length + fileData.Length;

            byte[] newSrrData = first.Concat(newHeader).Concat(fileData).Concat(after).ToArray();

            this._srrStream = new MemoryStream(newSrrData);

            using (FileStream fileStream = new FileStream(this._fileName, FileMode.OpenOrCreate | FileMode.Truncate, FileAccess.Write))
            {
                this._srrStream.CopyTo(fileStream);
            }
        }

        public void RenameFile(string oldFilename, string newFilename)
        {
            //TODO
            //useful? https://stackoverflow.com/questions/5733696/how-to-remove-data-from-a-memorystream
        }

        public void ReorderFiles(string[] orderedFilenames)
        {
            //TODO
        }

        public void RemoveStoredFile(string fileName)
        {
            List<SrrStoredFileBlock> srrStoredFileBlocks = this.BlockList.Where(x => x.SrrBlockHeader.BlockType == RarBlockType.SrrStoredFile).Select(x => x as SrrStoredFileBlock).ToList();

            foreach (SrrStoredFileBlock srrStoredFileBlock in srrStoredFileBlocks)
            {
                if (srrStoredFileBlock.FileName == fileName)
                {
                    byte[] srrBytes = this._srrStream.ToArray();
                    int remaning = srrBytes.Length - (int)srrStoredFileBlock.FileLength;

                    byte[] beforeBytes = srrBytes.Take((int)srrStoredFileBlock.BlockPosition).ToArray();
                    byte[] afterBytes = srrBytes.Skip((int)srrStoredFileBlock.BlockPosition).Skip((int)srrStoredFileBlock.SrrBlockHeader.FullSize).Take(remaning).ToArray();

                    byte[] newSrrData = beforeBytes.Concat(afterBytes).ToArray();

                    //overwrite memorystream
                    this._srrStream = new MemoryStream(newSrrData);

                    //write to file
                    using (FileStream fileStream = new FileStream(this._fileName, FileMode.OpenOrCreate | FileMode.Truncate, FileAccess.Write))
                    {
                        this._srrStream.CopyTo(fileStream);
                    }
                }
            }
        }

        public byte[] GetStoredFileData(string storedfileName)
        {
            SrrFileInfo srrFileInfo = this.StoredFileInfos.FirstOrDefault(x => x.FileName == storedfileName);

            if (srrFileInfo != null)
            {
                byte[] srrBytes = this._srrStream.ToArray(); //a bit ugly, can't use srrStream nor reader here, they are disposed

                byte[] storedFileData = srrBytes.Skip((int)srrFileInfo.FileOffset).Take((int)srrFileInfo.FileSize).ToArray();

                return storedFileData;
            }

            return null;
        }

        //processing
        private void ReadSrr(BinaryReader reader, bool calculateStoredCrc = false)
        {
            this.SrrSize = reader.BaseStream.Length;

            SrrRarFileBlock currentRarFile = null;
            long? currentPosition = null;

            reader.BaseStream.Position = 0; //make sure position is at the start

            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                if (currentPosition != null && currentPosition.Value >= reader.BaseStream.Position)
                {
                    //endless loop
                    break;
                }

                currentPosition = reader.BaseStream.Position;

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
                //TODO: SrrOsoHash check here is bad
                if (currentRarFile != null && srrBlockHeader.BlockType != RarBlockType.SrrRarFile && srrBlockHeader.BlockType != RarBlockType.SrrOsoHash)
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

                        if (calculateStoredCrc)
                        {
                            srrStoredFileBlock.FileCrc = Crc32Algorithm.Compute(this.GetStoredFileData(srrStoredFileBlock.FileName));
                        }

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
                        if (true)
                        {
                            //release with CMT: Bolgen.2015.NORWEGIAN.720p.BluRay.x264-WASTE , 2022-12-18 03:45:00
                            RarRecoveryBlock rarRecoveryBlock = new RarRecoveryBlock(srrBlockHeader, startOffset, ref reader);

                            this.BlockList.Add(rarRecoveryBlock);

                            //TODO: not a correct check
                            if (rarRecoveryBlock.FileName == "CMT")
                            {
                                reader.BaseStream.Seek(startOffset + rarRecoveryBlock.SrrBlockHeader.HeaderSize + (long)rarRecoveryBlock.PackedSize, SeekOrigin.Begin);
                            }
                            else
                            {
                                reader.BaseStream.Seek(startOffset + rarRecoveryBlock.SrrBlockHeader.HeaderSize, SeekOrigin.Begin);
                            }
                        }
                        else
                        {
                            this.BlockList.Add(new Block(srrBlockHeader, startOffset, ref reader));
                        }
                        break;
                    case RarBlockType.Unknown: //TODO: implement
                        break;
                    case RarBlockType.SrrOsoHash: //wont implement
                    case RarBlockType.RarMin:
                    case RarBlockType.RarMax:
                    case RarBlockType.OldComment:
                    case RarBlockType.OldAuthenticity1:
                    case RarBlockType.OldSubblock:
                    case RarBlockType.OldAuthenticity2:
                        reader.ReadBytes(srrBlockHeader.HeaderSize - 7); //skip block
                        break;
                    case RarBlockType.SrrRarPadding:
                        currentRarFile.FileLength -= srrBlockHeader.HeaderSize;
                        reader.BaseStream.Seek(startOffset + srrBlockHeader.FullSize, SeekOrigin.Begin);
                        break;
                    default:
                        //new Block(srrBlockHeader, startOffset, ref reader);
                        reader.BaseStream.Seek(startOffset + srrBlockHeader.FullSize, SeekOrigin.Begin);
                        break;
                }
            }

            //sfv processing
            foreach (SrrFileInfo storedFile in this.StoredFileInfos)
            {
                if (storedFile.FileName.ToLower().EndsWith(".sfv"))
                {
                    byte[] sfvData = this.GetStoredFileData(storedFile.FileName);
                    this.ProcessSfv(Encoding.UTF8.GetString(sfvData));
                }
            }
        }
    }
}
