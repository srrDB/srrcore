using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace srrcore.ReSample
{
    public class TrackData
    {
        public uint DataSize { get; set; }

        public uint SignatureSize { get; set; }

        public int MatchOffset { get; set; }

        public TrackData(byte[] data)
        {
            //header
            byte[] header = data.Take(2).ToArray();

            ushort flags = BitConverter.ToUInt16(header, 0);
            int extra = 0;
            uint trackNumber = 0;
            int add = 0;

            if ((flags & 0x8) == 0x8)
            {
                //4 bytes
                trackNumber = BitConverter.ToUInt32(data, 2);
                extra = 2;
            }
            else
            {
                //2 bytes
                trackNumber = BitConverter.ToUInt16(data, 2);
                extra = 0;
            }

            if ((flags & 0x4) == 0x4)
            {
                //big file
                header = data.Skip(4 + extra).Take(8).ToArray();

                uint low = BitConverter.ToUInt32(header);
                uint high = BitConverter.ToUInt32(header, 4);

                string lowHex = low.ToString("X").PadLeft(8, '0');
                string highHex = high.ToString("X");

                this.DataSize = (uint)int.Parse(highHex + lowHex, NumberStyles.HexNumber); //real data size?
                add = 8;
            }
            else
            {
                header = data.Skip(4 + extra).Take(4).ToArray();

                this.DataSize = BitConverter.ToUInt32(header);
                add = 4;
            }

            //ugly to remove? unnecessary scope
            {
                header = data.Skip(4 + extra + add).Take(10).ToArray();

                uint low = BitConverter.ToUInt32(header);
                uint high = BitConverter.ToUInt32(header, 4);
                uint signature = BitConverter.ToUInt16(header, 8);

                string lowHex = low.ToString("X").PadLeft(8, '0');
                string highHex = high.ToString("X");

                this.MatchOffset = int.Parse(highHex + lowHex, NumberStyles.HexNumber);
                this.SignatureSize = signature;
            }
        }
    }
}
