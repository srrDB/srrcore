using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading.Tasks;

namespace srrcore.ReSample
{
    public class FlacReader
    {
        private BinaryReader _reader;

        public long srsSize = 0;

        public bool readDone = true;

        public string blockType;

        public int blockLength = 0;

        public long blockStartPosition = 0;

        public FlacReader(BinaryReader reader)
        {
            _reader = reader;
            srsSize = reader.BaseStream.Length;
        }

        public bool Read()
        {
            this.blockStartPosition = this._reader.BaseStream.Position;
            this.readDone = false;

            if (this.blockStartPosition == this.srsSize)
            {
                return false;
            }

            byte[] header = this._reader.ReadBytes(4);
            string headerString = Encoding.UTF8.GetString(header);

            if (headerString == "fLaC")
            {
                this.blockType = "fLaC";
                this.blockLength = 0;

                return true;
            }

            if (headerString.StartsWith("ID3"))
            {
                this._reader.BaseStream.Seek(this.blockStartPosition + 6, SeekOrigin.Begin);
                int tagLen = calcDecTagLen(this._reader.ReadBytes(4));
                this.blockType = "ID3";
                this.blockLength = 10 + tagLen - 4;
                this._reader.BaseStream.Seek(this.blockStartPosition + 4, SeekOrigin.Begin);

                return true;
            }

            this.blockType = Encoding.UTF8.GetString(header.Take(1).ToArray());

            var testData = header.Take(4).ToArray();
            testData[0] = (byte)(char)'\0';
            testData = testData.Reverse().ToArray();

            uint size = BitConverter.ToUInt32(testData);
            this.blockLength = (int)size;

            return true;
        }

        public byte[] readContents()
        {
            this.readDone = true;

            return this._reader.ReadBytes(this.blockLength);
        }

        public void skipContents()
        {
            if (!this.readDone)
            {
                this.readDone = true;

                this._reader.BaseStream.Seek(this.blockLength, SeekOrigin.Current);
            }
        }

        public int calcDecTagLen(byte[] word)
        {
            int m = 1;
            int intt = 0;

            for (var i = word.Length - 1; i > -1; i--)
            {
                intt += m * (char)word[i];
                m = m * 128;
            }

            return intt;
        }
    }
}
