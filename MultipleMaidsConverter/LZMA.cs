using System;
using System.IO;
using SevenZip;
using SevenZip.Compression.LZMA;

namespace MultipleMaidsConverter
{
    internal static class LZMA
    {
        private const int Dictionary = 1 << 23;

        private static readonly CoderPropID[] PropIDs =
        {
        CoderPropID.DictionarySize,
        CoderPropID.PosStateBits,
        CoderPropID.LitContextBits,
        CoderPropID.LitPosBits,
        CoderPropID.Algorithm,
        CoderPropID.NumFastBytes,
        CoderPropID.MatchFinder,
        CoderPropID.EndMarker
    };

        private static readonly object[] Properties =
        {
        Dictionary,
        2,
        3,
        0,
        2,
        128,
        "bt4",
        false

    };

        public static byte[] Compress(MemoryStream inStream)
        {
            MemoryStream outStream = new MemoryStream();

            Encoder encoder = new Encoder();
            encoder.SetCoderProperties(PropIDs, Properties);
            encoder.WriteCoderProperties(outStream);

            Int64 fileSize = inStream.Length;

            for (int i = 0; i < 8; i++)
            {
                outStream.WriteByte((Byte)(fileSize >> (8 * i)));
            }

            encoder.Code(inStream, outStream, -1, -1, null);
            return outStream.ToArray();
        }

        public static MemoryStream Decompress(MemoryStream inStream)
        {
            MemoryStream outStream = new MemoryStream();

            byte[] properties = new byte[5];

            if (inStream.Read(properties, 0, 5) != 5)
            {
                throw new Exception("input .lzma is too short");
            }

            Decoder decoder = new Decoder();

            decoder.SetDecoderProperties(properties);

            long outSize = 0;

            for (int i = 0; i < 8; i++)
            {
                int v = inStream.ReadByte();
                if (v < 0)
                {
                    throw new Exception("Can't Read 1");
                }

                outSize |= ((long)(byte)v) << (8 * i);
            }

            long compressedSize = inStream.Length - inStream.Position;

            decoder.Code(inStream, outStream, compressedSize, outSize, null);

            return outStream;
        }
    }
}
