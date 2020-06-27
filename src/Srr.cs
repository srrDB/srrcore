using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Force.Crc32;

namespace SrrCore
{
    public class Srr
    {
        private const int HeaderLength = 7;

        public string ApplicationName;

        public bool Compressed;

        public long FileSize;

        //public uint FileCRC;

        public List<SrrFileInfo> StoredFileInfos = new List<SrrFileInfo>();

        public List<SrrFileInfo> RaredFileInfos = new List<SrrFileInfo>();

        public List<SrrFileInfo> ArchivedFileInfos = new List<SrrFileInfo>();

        private List<SrrFileInfo> SfvData = new List<SrrFileInfo>();

        private static List<SrrFileInfo> ProcessSfv(string fileData)
        {
            List<SrrFileInfo> sfvLinesData = new List<SrrFileInfo>();

            string[] sfvLines = fileData
                .Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries)
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

                    string fileName = sfvLine.Substring(0, spaceIndex);

                    //fix for if name is surrounded by quotations
                    if (fileName[0] == '"' && fileName[fileName.Length - 1] == '"')
                    {
                        fileName = fileName.Substring(1, fileName.Length - 1);
                    }

                    string crc = sfvLine.Substring(spaceIndex + 1, 8);

                    sfvLinesData.Add(new SrrFileInfo
                    {
                        FileName = fileName,
                        FileCrc = Convert.ToUInt32(crc, 16)
                    });
                }
            }

            return sfvLinesData;
        }

        public static Srr ReadSrr(string filename)
        {
            using (BinaryReader reader = new BinaryReader(File.Open(filename, FileMode.Open, FileAccess.Read, FileShare.Read)))
            {
                return ReadSrr(reader);
            }
        }

        public static Srr ReadSrr(Stream stream)
        {
            using (BinaryReader reader = new BinaryReader(stream))
            {
                return ReadSrr(reader);
            }
        }

        public static Srr ReadSrr(BinaryReader reader)
        {
            Srr srr = new Srr();

            SrrFileInfo rarFileInfo = null;

            srr.FileSize = reader.BaseStream.Length;
            //srr.FileCRC = Crc32Algorithm.Compute(reader.ReadBytes((int) reader.BaseStream.Length).ToArray());

            reader.BaseStream.Seek(0, SeekOrigin.Begin);

            while (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                long startOffset = reader.BaseStream.Position;

                byte[] headerBuff = new byte[HeaderLength];
                reader.Read(headerBuff, 0, HeaderLength); //read into buffer

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
                    default:
                        //minus headerLength because we already read that(?)
                        reader.BaseStream.Seek(header.HeaderSize + header.AddSize - HeaderLength, SeekOrigin.Current);

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
        }
    }
}