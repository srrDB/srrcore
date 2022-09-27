using System;
using System.IO;

namespace srrcore.ReSample
{
    public enum EbmlType
    {
        Block,
        Cluster,
        Segment,
        AttachmentList,
        ReSample,
        ReSampleFile,
        ReSampleTrack,
        Unknown
    }

    public class EbmlReader
    {
        public EbmlType EbmlType;

        private BinaryReader _reader;

        public long srsSize = 0;

        public bool readDone = true;

        //current block?
        public EbmlType? Etype = null;

        public int ElementLength = 0;

        public EbmlReader(BinaryReader reader)
        {
            _reader = reader;
            srsSize = reader.BaseStream.Length;
        }

        public bool Read()
        {
            if (_reader.BaseStream.Position + 2 > srsSize)
            {
                return false;
            }

            readDone = false;

            // element ID
            byte readByte = _reader.ReadByte();
            int idLengthDescriptor = getUIntLength(readByte);

            string elementHeader = readByte.ToString("X").PadLeft(2, '0');
            if (idLengthDescriptor > 1)
            {
                elementHeader += BitConverter.ToString(_reader.ReadBytes(idLengthDescriptor - 1)).Replace("-", string.Empty);
            }

            // data size
            readByte = _reader.ReadByte();
            int dataLengthDescriptor = getUIntLength(readByte);
            elementHeader += readByte.ToString("X").PadLeft(2, '0');
            if (dataLengthDescriptor > 1)
            {
                elementHeader += BitConverter.ToString(_reader.ReadBytes(dataLengthDescriptor - 1)).Replace("-", string.Empty);
            }

            if (idLengthDescriptor + dataLengthDescriptor != elementHeader.Length / 2)
            {
                return false;
            }

            string eh = elementHeader.Substring(0, 2 * idLengthDescriptor).ToUpper();

            switch (eh)
            {
                case "A1":
                case "A2":
                    Etype = EbmlType.Block;
                    break;
                case "1F43B675":
                    Etype = EbmlType.Cluster;
                    break;
                case "18538067":
                    Etype = EbmlType.Segment;
                    break;
                case "1941A469":
                    Etype = EbmlType.AttachmentList;
                    break;
                case "1F697576":
                    Etype = EbmlType.ReSample;
                    break;
                case "6A75":
                    Etype = EbmlType.ReSampleFile;
                    break;
                case "6B75":
                    Etype = EbmlType.ReSampleTrack;
                    break;
                default:
                    Etype = EbmlType.Unknown;
                    break;
            }

            ElementLength = getEbmlUInt(elementHeader, idLengthDescriptor, dataLengthDescriptor);

            return true;
        }

        public void SkipContents()
        {
            if (!readDone)
            {
                readDone = true;

                if (Etype != EbmlType.Block)
                {
                    _reader.BaseStream.Seek(ElementLength, SeekOrigin.Current);
                }
            }
        }

        public void moveToChild()
        {
            readDone = true;
        }

        public byte[] readContents()
        {
            if (readDone)
            {
                _reader.BaseStream.Seek(ElementLength, SeekOrigin.Current);
            }

            readDone = true;
            byte[] buffer = null;

            // skip over removed ebml elements
            if (Etype != EbmlType.Block)
            {
                buffer = _reader.ReadBytes(ElementLength);
            }
            return buffer;
        }

        private int getUIntLength(byte lengthDescriptor)
        {
            int length = 0;
            for (var i = 0; i < 8; i++)
            {
                if ((lengthDescriptor & 0x80 >> i) != 0)
                {
                    length = i + 1;
                    break;
                }
            }
            return length;
        }

        private int getEbmlUInt(string buff, int offset, int count)
        {
            int size = Convert.ToInt32(buff.Substring(offset * 2, 2), 16) & 0xFF >> count;

            for (int i = 1; i < count; i++)
            {
                size = (size << 8) + Convert.ToInt32(buff.Substring(offset * 2 + i * 2, 2), 16);
            }

            return size;
        }
    }
}
