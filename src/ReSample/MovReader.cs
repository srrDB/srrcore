using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading.Tasks;

namespace srrcore.ReSample
{
    public class MovReader
    {
        private BinaryReader _reader;

        public long srsSize = 0;

        public bool readDone = true;

        public string atomType;

        public long atomLength = 0;

        public long atomStartPosition = 0;

        public long headerSize = 0;

        public MovReader(BinaryReader reader)
        {
            _reader = reader;
            srsSize = reader.BaseStream.Length;
        }

        public bool Read()
        {
            long atomStartPosition = this._reader.BaseStream.Position;
            this.readDone = false;

            if (atomStartPosition + 8 > this.srsSize)
            {
                return false;
            }

            byte[] header = _reader.ReadBytes(8);

            long atomLength = BitConverter.ToUInt32(header.Take(4).Reverse().ToArray());
            this.atomType = Encoding.UTF8.GetString(header.Skip(4).Take(4).ToArray());

            // special sizes
            int hsize = 8;

            if (atomLength == 1)
            {
                // 8-byte size field after the atom type
                header = this._reader.ReadBytes(8);

                uint high = BitConverter.ToUInt32(header.Take(4).Reverse().ToArray());
                uint low = BitConverter.ToUInt32(header.Skip(4).Take(4).Reverse().ToArray());

                // add the high order bits before the low order bits and convert to decimal
                string lowHex = low.ToString("X").PadLeft(8, '0');
                string highHex = high.ToString("X");

                atomLength = (uint)int.Parse(highHex + lowHex, NumberStyles.HexNumber);
                hsize += 8;
            }
            else if (atomLength == 0)
            {
                // FoV/COMPULSiON samples have an atom that consists of just 8 null bytes.
                // This is the case if it is followed by an mdat
                if (this.atomType == "\x00\x00\x00\x00")
                {
                    atomLength = 8;
                }
                else
                {
                    // the atom extends to the end of the file
                    atomLength = this.srsSize - atomStartPosition;
                }
            }

            this.headerSize = hsize;
            this.atomLength = atomLength;
            this.atomStartPosition = atomStartPosition;

            this._reader.BaseStream.Seek(atomStartPosition, SeekOrigin.Begin);

            return true;
        }

        public byte[] readContents()
        {
            if (this.readDone)
            {
                this._reader.BaseStream.Seek(this.atomStartPosition, SeekOrigin.Begin);
            }

            this.readDone = true;

            byte[] buffer = null;

            this._reader.BaseStream.Seek(this.headerSize, SeekOrigin.Current);

            if (this.atomType != "mdat")
            {
                buffer = this._reader.ReadBytes((int)(this.atomLength - this.headerSize));
            }

            return buffer;
        }

        public void skipContents()
        {
            if (!this.readDone)
            {
                this.readDone = true;

                if (this.atomType != "mdat")
                {
                    this._reader.BaseStream.Seek(this.atomLength, SeekOrigin.Current);
                }
                else
                {
                    this._reader.BaseStream.Seek(this.headerSize, SeekOrigin.Current);
                }
            }
        }

        public void moveToChild()
        {
            this.readDone = true;
            this._reader.BaseStream.Seek(this.headerSize, SeekOrigin.Current);
        }
    }
}
