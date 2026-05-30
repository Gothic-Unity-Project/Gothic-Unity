using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using JetBrains.Annotations;

namespace GUZ.Core.Domain.Audio
{
    public class ImaadpcmDecoderDomain
    {
        private enum WaveFormat
        {
            Unknown,
            PCM,
            Adpcm,
            ImaAdpcm = 0x11
        }

        public byte[] CreatePCMHeader(int sampleRate, int channels, int bitsPerSample, int dataSize)
        {
            var header = new List<byte>();

            header.AddRange(Encoding.ASCII.GetBytes("RIFF"));
            var fileSize = dataSize + 36;
            header.AddRange(BitConverter.GetBytes(fileSize));
            header.AddRange(Encoding.ASCII.GetBytes("WAVE"));
            header.AddRange(Encoding.ASCII.GetBytes("fmt "));
            header.AddRange(BitConverter.GetBytes(16));
            header.AddRange(BitConverter.GetBytes((short)1));
            header.AddRange(BitConverter.GetBytes((short)channels));
            header.AddRange(BitConverter.GetBytes(sampleRate));
            var byteRate = sampleRate * channels * bitsPerSample / 8;
            header.AddRange(BitConverter.GetBytes(byteRate));
            var blockAlign = (short)(channels * bitsPerSample / 8);
            header.AddRange(BitConverter.GetBytes(blockAlign));
            header.AddRange(BitConverter.GetBytes((short)bitsPerSample));
            header.AddRange(Encoding.ASCII.GetBytes("data"));
            header.AddRange(BitConverter.GetBytes(dataSize));
            return header.ToArray();
        }

        private int ReadHeader(Stream stream, bool forDecode, bool forceMonoEncode)
        {
            var riffLength = 0;
            if (ReadId(stream) != "RIFF")
            {
                throw new ApplicationException("Invalid RIFF header");
            }

            riffLength = ReadInt32(stream);
            if (ReadId(stream) != "WAVE")
            {
                throw new ApplicationException("Wave type is expected");
            }

            var fmtSize = 0;
            _dataSize = 0;
            while (stream.Position < stream.Length)
            {
                switch (ReadId(stream))
                {
                    case "fmt ":
                        fmtSize = ReadInt32(stream);
                        if (forDecode)
                        {
                            if (ReadUInt16(stream) != (ushort)WaveFormat.ImaAdpcm)
                            {
                                throw new ApplicationException("Not IMA ADPCM");
                            }
                        }
                        else
                        {
                            if (ReadUInt16(stream) != (ushort)WaveFormat.PCM)
                            {
                                throw new ApplicationException("Not PCM");
                            }
                        }

                        _inChannels = ReadUInt16(stream);
                        _samplesPerSecond = ReadInt32(stream);
                        ReadInt32(stream);
                        _blockAlign = ReadUInt16(stream);
                        if (forDecode)
                        {
                            if (ReadUInt16(stream) != 4)
                            {
                                throw new ApplicationException("Not 4-bit format");
                            }
                        }
                        else
                        {
                            if (ReadUInt16(stream) != 16)
                            {
                                throw new ApplicationException("Not 16-bit format");
                            }
                        }

                        ReadBytes(stream, fmtSize - 16);
                        break;
                    case "data":
                        _dataSize = ReadInt32(stream);
                        _offset = (int)stream.Position;
                        stream.Position += _dataSize;
                        break;
                    default:
                        var size = ReadInt32(stream);
                        stream.Position += size;
                        break;
                }
            }

            if (fmtSize == 0)
            {
                throw new ApplicationException("No format information");
            }

            if (_dataSize == 0)
            {
                throw new ApplicationException("No data");
            }

            var blocks = _dataSize / _blockAlign;
            int blocklen;
            int dataLength;
            int bytesPerSecond;

            _outChannels = forceMonoEncode && !forDecode ? (ushort)1 : _inChannels;

            if (forDecode)
            {
                blocklen = (_blockAlign - _inChannels * 4) * 4 +
                           _inChannels * 2; // 4=bits 2 = 16bit (2 bytes)  - How much to pull from source stream
                dataLength = blocks * blocklen;
                bytesPerSecond = _samplesPerSecond * _outChannels * 2;
            }
            else
            {
                _imaBlockAlign = 36 * _outChannels;

                // compressed data without header (4 is header per channel)
                var imaDataOnly = _imaBlockAlign - _outChannels * 4;

                //(How many uncompressed samples fit in a block) + (how many headers)
                dataLength = _dataSize / (imaDataOnly * 4) * imaDataOnly +
                             _dataSize / (imaDataOnly * 4) * _outChannels * 4;

                // crop off any decimal points.  Each channel will shrink by 1 quarter + 4 bytes per block + channel
                bytesPerSecond = (int)(_samplesPerSecond * 0.5625 * _outChannels);

                _predictedValues = new short[_outChannels];
                _stepIndexes = new int[_outChannels];
            }

            if (_inChannels > _outChannels)
            {
                dataLength /= 2;
            }

            _length = dataLength + (!forDecode ? 48 : 44);

            _header = CreatePCMHeader(_samplesPerSecond, _outChannels, 16, dataLength);

            return _header.Length;
        }

        public byte[] Decode(byte[] srcBytes)
        {
            using (var s = new MemoryStream(srcBytes))
            {
                ReadHeader(s, true, false);
                var decodedData = new List<byte>(_header.Length + _dataSize);

                decodedData.AddRange(_header);

                var blocks = _dataSize / _blockAlign;

                for (var i = 0; i < blocks; i++)
                {
                    var block = DecodeBlock(s, i);
                    decodedData.AddRange(block);
                }

                _header = null;
                return decodedData.ToArray();
            }

            // Clean up resources here if needed
        }

        [CanBeNull]
        private byte[] DecodeBlock(Stream stream, int source)
        {
            if (source >= _dataSize / _blockAlign)
            {
                return null;
            }

            if (_cacheNo == source)
            {
                return _cache;
            }

            var position = _offset + source * _blockAlign; //4 = compression ratio
            if (position >= stream.Length)
            {
                return null;
            }

            stream.Position = position;
            var data = ReadBytes(stream, _blockAlign);

            using (var memStream = new MemoryStream())
            {
                using (var writer = new BinaryWriter(memStream))
                {
                    var values = new SampleValue[_outChannels];
                    for (var channel = 0; channel < _outChannels; channel++)
                    {
                        values[channel] = new SampleValue(data, channel * 4);
                    }

                    var bytesPerChannelBlock = _outChannels * 4;

                    if (_outChannels == 1) //mono
                    {
                        for (var i = bytesPerChannelBlock; i < _blockAlign; i++)
                        {
                            writer.Write(values[0].DecodeNext(data[i] & 0xf));
                            writer.Write(values[0].DecodeNext(data[i] >> 4));
                        }
                    }
                    else
                    {
                        for (var i = bytesPerChannelBlock; i < _blockAlign; i += bytesPerChannelBlock)
                        {
                            for (var j = 0; j < 4; j++)
                            {
                                for (var channel = 0; channel < _outChannels; channel++)
                                {
                                    writer.Write(values[channel].DecodeNext(data[i + j + channel * 4] & 0xf));
                                }

                                for (var channel = 0; channel < _outChannels; channel++)
                                {
                                    writer.Write(values[channel].DecodeNext(data[i + j + channel * 4] >> 4));
                                }
                            }
                        }
                    }

                    _cacheNo = source;
                    _cache = memStream.ToArray();
                }
            }

            return _cache;
        }

        private struct SampleValue
        {
            public short PredictedValue;
            public int StepIndex;

            public SampleValue(short predictedValue, int stepIndex)
            {
                PredictedValue = predictedValue;
                StepIndex = stepIndex;
            }

            public SampleValue(byte[] value, int stepIndex)
            {
                PredictedValue = BitConverter.ToInt16(value, stepIndex);
                StepIndex = value[stepIndex + 2];
            }

            private static readonly int[] _stepTable =
            {
                7, 8, 9, 10, 11, 12, 13, 14,
                16, 17, 19, 21, 23, 25, 28, 31,
                34, 37, 41, 45, 50, 55, 60, 66,
                73, 80, 88, 97, 107, 118, 130, 143,
                157, 173, 190, 209, 230, 253, 279, 307,
                337, 371, 408, 449, 494, 544, 598, 658,
                724, 796, 876, 963, 1060, 1166, 1282, 1411,
                1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024,
                3327, 3660, 4026, 4428, 4871, 5358, 5894, 6484,
                7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899,
                15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794,
                32767
            };

            private static readonly int[] _indexTable =
            {
                -1, -1, -1, -1, 2, 4, 6, 8,
                -1, -1, -1, -1, 2, 4, 6, 8
            };

            public short DecodeNext(int adpcm)
            {
                var step = _stepTable[StepIndex];
                var diff = ((((adpcm & 7) << 1) + 1) * step) >> 3;

                if ((adpcm & 8) != 0)
                {
                    diff = -diff;
                }

                var predictedValue = PredictedValue + diff;

                PredictedValue = (short)Math.Clamp(predictedValue, short.MinValue, short.MaxValue);


                var idx = StepIndex + _indexTable[adpcm];
                if (idx >= _stepTable.Length)
                {
                    idx = _stepTable.Length - 1;
                }

                if (idx < 0)
                {
                    idx = 0;
                }

                StepIndex = idx;
                return PredictedValue;
            }
        }

        private byte[] ReadBytes(Stream s, int length)
        {
            var ret = new byte[length];
            if (length > 0)
            {
                s.Read(ret, 0, length);
            }

            return ret;
        }

        private string ReadId(Stream s)
        {
            return Encoding.UTF8.GetString(ReadBytes(s, 4), 0, 4);
        }

        private int ReadInt32(Stream s)
        {
            return BitConverter.ToInt32(ReadBytes(s, 4), 0);
        }

        private ushort ReadUInt16(Stream s)
        {
            return BitConverter.ToUInt16(ReadBytes(s, 2), 0);
        }

        private int _length;
        private ushort _inChannels;
        private ushort _outChannels;
        private int _samplesPerSecond;
        private ushort _blockAlign;
        private int _offset;
        private int _dataSize;
        [CanBeNull] private byte[] _header;
        private int _cacheNo = -1;
        private byte[] _cache;

        private int _imaBlockAlign;
        private short[] _predictedValues;
        private int[] _stepIndexes;
    }
}
