using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading.Tasks;
using System.Globalization;
using srrcore.ReSample;
using System.Collections;
using System.Drawing;

namespace srrcore
{
    public enum FileType
    {
        Unknown,
        MKV,
        AVI,
        WMV,
        FLAC,
        MP3,
        STREAM,
        MP4
    }

    public class Srs
    {
        public string _fileName { get; set; }

        private BinaryReader _reader;

        private MemoryStream _srsStream = new MemoryStream();

        public Srs(string fileName)
        {
            this._fileName = fileName;

            //copy file to a memorystream that will be used instead of the file
            using (FileStream fileStream = File.Open(this._fileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                this._srsStream = new MemoryStream();
                this._srsStream.SetLength(fileStream.Length);

                fileStream.CopyTo(this._srsStream);
            }

            using (this._reader = new BinaryReader(this._srsStream))
            {
                this._reader.BaseStream.Position = 0; //make sure position is at the start

                FileType fileType = this.DetectFileFormat();

                if (fileType == FileType.MKV)
                {
                    ReadMkv();
                }
                else if (fileType == FileType.AVI)
                {
                    ReadAvi();
                }
                else if (fileType == FileType.MP4)
                {
                    ReadMp4();
                }
                else if (fileType == FileType.WMV)
                {
                    ReadWmv();
                }
                else if (fileType == FileType.FLAC)
                {
                    ReadFlac();
                }
            }
        }

        public void ReadFlac()
        {
            FlacReader flacReader = new FlacReader(this._reader);

            while (flacReader.Read())
            {
                if (flacReader.blockType == "s")
                {
                    byte[] data = flacReader.readContents();
                    FileData fs = new FileData(data);
                }
                else if (flacReader.blockType == "t")
                {
                    byte[] data = flacReader.readContents();
                    TrackData td = new TrackData(data);
                }
                else if (flacReader.blockType == "u")
                {
                    byte[] data = flacReader.readContents();

                    long duration = BitConverter.ToUInt32(data);
                    long fplength = BitConverter.ToUInt32(data, 4);

                    string fingerprint = Encoding.UTF8.GetString(data.Skip(8).Take((int)fplength).ToArray());
                }
                else
                {
                    flacReader.skipContents();
                }

                // mandatory STREAMINFO metadata block encountered
                if (flacReader.blockType == "\0")
                {
                    break; // stop parsing FLAC file
                }
            }
        }

        public void ReadWmv()
        {
            string GUID_SRS_FILE = "SRSFSRSFSRSFSRSF";
            string GUID_SRS_TRACK = "SRSTSRSTSRSTSRST";
            string GUID_SRS_PADDING = "PADDINGBYTESDATA";

            long srsSize = this._reader.BaseStream.Length;

            byte[] data = this._reader.ReadBytes((int)srsSize);

            long startpos = Srs.IndexOf(data, Encoding.UTF8.GetBytes(GUID_SRS_FILE));

            this._reader.BaseStream.Seek(startpos, SeekOrigin.Begin);

            while (startpos < srsSize)
            {
                string guid = Encoding.UTF8.GetString(this._reader.ReadBytes(16));

                byte[] header = this._reader.ReadBytes(8);

                uint low = BitConverter.ToUInt32(header);
                uint high = BitConverter.ToUInt32(header, 4);
                // add the high order bits before the low order bits and convert to decimal
                string lowHex = low.ToString("X").PadLeft(8, '0');
                string highHex = high.ToString("X");

                int size = int.Parse(highHex + lowHex, NumberStyles.HexNumber); //real sample size?

                //new header
                header = this._reader.ReadBytes(size - 24);

                if (guid == GUID_SRS_FILE)
                {
                    FileData fs = new FileData(header);
                }
                else if (guid == GUID_SRS_TRACK)
                {
                    TrackData td = new TrackData(header);
                }
                else if (guid == GUID_SRS_PADDING)
                {
                    int paddingSize = size - 24;
                }
                else
                {
                    break;
                }

                startpos = this._reader.BaseStream.Position;
            }

        }

        public void ReadMp4()
        {
            MovReader movReader = new MovReader(this._reader);

            while (movReader.Read())
            {
                if (movReader.atomType == "SRSF")
                {
                    byte[] data = movReader.readContents();
                    FileData fs = new FileData(data);
                }
                else if (movReader.atomType == "SRST")
                {
                    byte[] data = movReader.readContents();
                    TrackData td = new TrackData(data);
                }
                else if (movReader.atomType == "mdat")
                {
                    movReader.moveToChild();
                }
                else
                {
                    movReader.skipContents();
                }
            }
        }

        public void ReadAvi()
        {
            RiffReader riffReader = new RiffReader(this._reader);
            bool done = false;

            while (!done && riffReader.Read())
            {
                if (riffReader.ChunkType == ChunkType.LIST)
                {
                    riffReader.moveToChild();
                }
                else
                {
                    if (riffReader.fourcc == "SRSF") //sample file?
                    {
                        byte[] data = riffReader.readContents();
                        FileData fs = new FileData(data);
                    }
                    else if (riffReader.fourcc == "SRST") //sample track?
                    {
                        byte[] data = riffReader.readContents();
                        TrackData td = new TrackData(data);
                    }
                    else if (riffReader.ChunkType == ChunkType.MOVI)
                    {
                        done = true;
                        break;
                    }
                    else
                    {
                        riffReader.skipContents();
                    }
                }
            }
        }

        public void ReadMkv()
        {
            EbmlReader ebmlReader = new EbmlReader(this._reader);
            bool done = false;

            while (!done && ebmlReader.Read())
            {
                if (ebmlReader.Etype == EbmlType.Segment || ebmlReader.Etype == EbmlType.ReSample)
                {
                    ebmlReader.moveToChild();
                }
                else if (ebmlReader.Etype == EbmlType.ReSampleFile)
                {
                    byte[] data = ebmlReader.readContents();
                    FileData fs = new FileData(data);
                }
                else if (ebmlReader.Etype == EbmlType.ReSampleTrack)
                {
                    byte[] data = ebmlReader.readContents();
                    TrackData td = new TrackData(data);
                }
                else if (ebmlReader.Etype == EbmlType.Cluster || ebmlReader.Etype == EbmlType.AttachmentList)
                {
                    ebmlReader.SkipContents();
                    done = true;
                }
                else
                {
                    ebmlReader.SkipContents();
                }
            }
        }

        public FileType DetectFileFormat()
        {
            FileType ftReturn = FileType.Unknown;

            byte[] firstBytes = this._reader.ReadBytes(4);
            string hex = BitConverter.ToString(firstBytes).Replace("-", string.Empty);

            if (hex == "1A45DFA3")
            {
                ftReturn = FileType.MKV;
            }
            else if (hex == "52494646") //RIFF
            {
                ftReturn = FileType.AVI;
            }
            else if (hex == "3026B275")
            {
                ftReturn = FileType.WMV;
            }
            else if (hex == "664C6143")
            {
                ftReturn = FileType.FLAC;
            }
            else if (hex == "53525346")
            {
                ftReturn = FileType.MP3;
            }
            else if (hex == "5354524D")
            {
                ftReturn = FileType.STREAM;
            }
            else
            {
                byte[] nextBytes = this._reader.ReadBytes(4);
                string nextHex = BitConverter.ToString(nextBytes).Replace("-", string.Empty);

                if (nextHex == "66747970")
                {
                    ftReturn = FileType.MP4;
                }
                else if (hex.Substring(0, 6) == "494433") //ID3
                {
                    // can be MP3 or FLAC
                    //TODO
                }
            }

            this._reader.BaseStream.Seek(0, SeekOrigin.Begin);
            return ftReturn;
        }

        public static int IndexOf(byte[] arrayToSearchThrough, byte[] patternToFind)
        {
            if (patternToFind.Length > arrayToSearchThrough.Length)
                return -1;
            for (int i = 0; i < arrayToSearchThrough.Length - patternToFind.Length; i++)
            {
                bool found = true;
                for (int j = 0; j < patternToFind.Length; j++)
                {
                    if (arrayToSearchThrough[i + j] != patternToFind[j])
                    {
                        found = false;
                        break;
                    }
                }
                if (found)
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
