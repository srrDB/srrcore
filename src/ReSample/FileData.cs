using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace srrcore.ReSample
{
    public class FileData
    {
        public string AppName { get; set; }

        public string Filename { get; set; }

        public ulong Filesize { get; set; }

        public string Crc32 { get; set; }

        public FileData(byte[] data)
        {
            //header
            byte[] header = data.Take(4).ToArray();

            ushort flags = BitConverter.ToUInt16(header, 0);
            ushort appLength = BitConverter.ToUInt16(header, 2);

            this.AppName = Encoding.UTF8.GetString(data.Skip(4).Take(appLength).ToArray()); //software name

            //new header
            header = data.Skip(4 + appLength).Take(2).ToArray();

            ushort nameLength = BitConverter.ToUInt16(header, 0);

            this.Filename = Encoding.UTF8.GetString(data.Skip(4 + appLength + 2).Take(nameLength).ToArray()); //sample name

            int offset = 4 + appLength + 2 + nameLength;

            //new header
            header = data.Skip(offset).Take(12).ToArray();

            uint low = BitConverter.ToUInt32(header);
            uint high = BitConverter.ToUInt32(header, 4);
            uint crc32 = BitConverter.ToUInt32(header, 8);

            string lowHex = low.ToString("X").PadLeft(8, '0');
            string highHex = high.ToString("X");

            this.Filesize = (ulong)int.Parse(highHex + lowHex, NumberStyles.HexNumber); //real sample size
            this.Crc32 = crc32.ToString("X").PadLeft(8, '0'); //real sample crc
        }
    }
}
