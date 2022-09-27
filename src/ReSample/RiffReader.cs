using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace srrcore.ReSample
{
    public enum ChunkType
    {
        Unknown,
        LIST,
        MOVI
    }

    public class RiffReader
    {
        private BinaryReader _reader;

        public long srsSize = 0;

        public bool readDone = true;

        public ChunkType ChunkType;

        public bool hasPadding = false;

        public uint chunkLength = 0;

        public string fourcc = "";

        public RiffReader(BinaryReader reader)
        {
            _reader = reader;
            srsSize = reader.BaseStream.Length;
        }

        public bool Read()
        {
            long chunkStartPosition = _reader.BaseStream.Position;
            readDone = false;

            if (chunkStartPosition + 8 > srsSize)
            {
                return false;
            }

            byte[] header = _reader.ReadBytes(8);

            fourcc = Encoding.UTF8.GetString(header.Take(4).ToArray());
            chunkLength = BitConverter.ToUInt32(header, 4);

            if (fourcc == "RIFF" || fourcc == "LIST")
            {
                _reader.BaseStream.Seek(4, SeekOrigin.Current);
                chunkLength -= 4;
                ChunkType = ChunkType.LIST;
            }
            else
            {
                if (int.TryParse(Encoding.UTF8.GetString(header.Take(2).ToArray()), out int tempo))
                {
                    ChunkType = ChunkType.MOVI;
                }
                else
                {
                    ChunkType = ChunkType.Unknown;
                }
            }

            hasPadding = chunkLength % 2 == 1;

            return true;
        }

        public byte[] readContents()
        {
            if (readDone)
            {
                _reader.BaseStream.Seek(-chunkLength - (hasPadding ? 1 : 0), SeekOrigin.Current);
            }

            readDone = true;
            byte[] buffer = null;

            if (ChunkType != ChunkType.MOVI)
            {
                buffer = _reader.ReadBytes((int)chunkLength);
            }

            if (hasPadding)
            {
                _reader.BaseStream.Seek(1, SeekOrigin.Current);
            }

            return buffer;
        }

        public void skipContents()
        {
            if (!readDone)
            {
                readDone = true;

                if (ChunkType != ChunkType.MOVI)
                {
                    _reader.BaseStream.Seek(chunkLength, SeekOrigin.Current);
                }

                if (hasPadding)
                {
                    _reader.BaseStream.Seek(1, SeekOrigin.Current);
                }
            }
        }

        public void moveToChild()
        {
            readDone = true;
        }
    }
}
