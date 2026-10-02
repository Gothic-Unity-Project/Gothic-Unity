// Bink video decoder.
// C# port of OpenGothic common/bink/video.cpp (https://github.com/Try/OpenGothic, MIT repository), whose codec is
// itself based on FFmpeg libavcodec/bink.c and binkaudio.c - licensed LGPL 2.1 or later. See README.md in this folder.
using System;
using System.IO;
using static Gothic.Core.Bink.BinkTables;

namespace Gothic.Core.Bink
{
    /// <summary>
    /// Decodes a Bink (.bik) file frame by frame into raw YUV420 planes + float PCM audio. No Unity dependencies.
    /// Usage: new BinkVideo(stream); then NextFrame() up to FrameCount times, every 1/Fps seconds.
    /// </summary>
    public partial class BinkVideo
    {
        private const uint _binkTag = 1766541634; // "BIKi"
        private const uint _flagAlpha = 0x00100000;
        private const ushort _audioStereo = 0x2000;
        private const ushort _audioUseDct = 0x1000;
        private const int _dcStartBits = 11;

        // Bundles (sources of values for the block decoding).
        private const int _srcBlockTypes = 0;
        private const int _srcSubBlockTypes = 1;
        private const int _srcColors = 2;
        private const int _srcPattern = 3;
        private const int _srcXOff = 4;
        private const int _srcYOff = 5;
        private const int _srcIntraDc = 6;
        private const int _srcInterDc = 7;
        private const int _srcRun = 8;
        private const int _srcCount = 9;

        private enum BlockType
        {
            Skip = 0, Scaled, Motion, Run, Residue, Intra, Fill, Inter, Pattern, Raw
        }

        private struct IndexEntry
        {
            public uint Position;
            public uint Size;
        }

        private class Tree
        {
            public int VlcNum;
            public readonly byte[] Symbols = new byte[16];
        }

        private class Bundle
        {
            public int Length; // number of bits of the "count of entries" field
            public readonly Tree Tree = new();
            public byte[] Data = Array.Empty<byte>();
            public int CurrentDecode; // -1 = done for this plane (nullptr in the original)
            public int CurrentRead;
        }

        // VLC tree parameters: bits and offset into VlcTable per tree.
        private static readonly int[] _treeBits = { 4, 5, 5, 5, 5, 5, 5, 6, 6, 6, 6, 6, 6, 7, 7, 7 };

        private readonly Stream _input;
        private readonly byte _revision;
        private readonly uint _flags;
        private readonly int _width;
        private readonly int _height;
        private readonly IndexEntry[] _index;

        private readonly BinkFrame[] _frames = { new(), new() };
        private byte[] _packet = new byte[64 * 1024];
        private int _frameCounter;

        private readonly Bundle[] _bundles = new Bundle[_srcCount];
        private readonly Tree[] _colHigh = new Tree[16];
        private int _colLastValue;

        // Reused per block.
        private readonly byte[] _block = new byte[64];
        private readonly byte[] _previous = new byte[64];
        private readonly int[] _dctBlock = new int[64];
        private readonly int[] _temp = new int[64];
        private readonly short[] _residue = new short[64];
        private readonly int[] _coefIndex = new int[64];

        public int Width => _width;
        public int Height => _height;
        public int FrameCount => _index.Length;
        public int CurrentFrame => _frameCounter;
        public uint FpsNumerator { get; }
        public uint FpsDenominator { get; }
        public double Fps => (double)FpsNumerator / FpsDenominator;

        public BinkVideo(Stream input)
        {
            _input = input;

            var codec = ReadUInt32();
            if ((codec & 0xFFFFFF) != 0x4B4942 /* "BIK" */ && (codec & 0xFFFFFF) != 0x32424B /* "KB2" */)
                throw new BinkException("Not a Bink file.");

            var fileSize = ReadUInt32() + 8;
            var duration = ReadUInt32();
            if (ReadUInt32() > fileSize)
                throw new BinkException("Invalid header: largest frame size greater than file size.");
            ReadUInt32();

            _width = (int)ReadUInt32();
            _height = (int)ReadUInt32();
            FpsNumerator = ReadUInt32();
            FpsDenominator = ReadUInt32();
            if (FpsNumerator == 0 || FpsDenominator == 0)
                throw new BinkException($"Invalid fps {FpsNumerator}/{FpsDenominator}.");

            _flags = ReadUInt32();
            var audioTrackCount = ReadUInt32();

            var signature = codec & 0xFFFFFF;
            _revision = (byte)((codec >> 24) % 0xFF);
            if ((signature == 0x4B4942 && _revision == 'k') ||
                (signature == 0x32424B && (_revision == 'i' || _revision == 'j' || _revision == 'k')))
                ReadUInt32(); // unknown new field

            if (audioTrackCount > 0)
            {
                _input.Seek(4 * audioTrackCount, SeekOrigin.Current); // max decoded size
                for (var i = 0; i < audioTrackCount; i++)
                {
                    var sampleRate = ReadUInt16();
                    var audioFlags = ReadUInt16();
                    var isStereo = (audioFlags & _audioStereo) != 0;
                    _audioTracks.Add(new AudioContext(sampleRate, isStereo ? 2 : 1, (audioFlags & _audioUseDct) != 0));
                    AudioTracks.Add(new AudioTrackInfo { SampleRate = sampleRate, IsStereo = isStereo });
                }
                for (var i = 0; i < audioTrackCount; i++)
                    ReadUInt32(); // track id
            }

            // Frame index table. Bit 0 of a position = key frame.
            _index = new IndexEntry[duration];
            var nextPosition = ReadUInt32();
            for (var i = 0; i < duration; i++)
            {
                var position = nextPosition & ~1u;
                nextPosition = i + 1 == duration ? fileSize : ReadUInt32();
                var next = nextPosition & ~1u;
                if (next <= position)
                    throw new BinkException("Invalid frame index table.");
                _index[i] = new IndexEntry { Position = position, Size = next - position };
            }

            DecodeInit();
            foreach (var track in _audioTracks)
                DecodeAudioInit(track);
            foreach (var frame in _frames)
                frame.SetAudioChannels(_audioTracks.Count);
        }

        /// <summary>
        /// Decodes the next frame. The returned frame is reused - copy what you need before the next call.
        /// </summary>
        public BinkFrame NextFrame()
        {
            if (_frameCounter >= _index.Length)
                return _frames[_frameCounter % 2];

            try
            {
                ReadPacket();
            }
            finally
            {
                _frameCounter++;
            }
            return _frames[(_frameCounter - 1) % 2];
        }

        private void ReadPacket()
        {
            var entry = _index[_frameCounter];
            _input.Seek(entry.Position, SeekOrigin.Begin);

            var videoSize = entry.Size;
            var frame = _frames[_frameCounter % 2];
            for (var i = 0; i < _audioTracks.Count; i++)
            {
                var audioSize = ReadUInt32();
                if (audioSize + 4 > videoSize)
                    throw new BinkException($"Audio size {audioSize} > packet left {videoSize}.");

                if (audioSize >= 4)
                {
                    ReadIntoPacket((int)audioSize);
                    ParseAudio((int)audioSize, i, frame.Audio[i]);
                }
                else
                {
                    _input.Seek(audioSize, SeekOrigin.Current);
                    frame.Audio[i].Clear();
                }
                videoSize -= audioSize + 4;
            }

            ReadIntoPacket((int)videoSize);
            ParseFrame((int)videoSize);
        }

        private void ReadIntoPacket(int size)
        {
            if (_packet.Length < size)
                _packet = new byte[size];

            var read = 0;
            while (read < size)
            {
                var count = _input.Read(_packet, read, size - read);
                if (count <= 0)
                    throw new BinkException("Unexpected end of file.");
                read += count;
            }
        }

        private uint ReadUInt32()
        {
            Span<byte> buffer = stackalloc byte[4];
            if (_input.Read(buffer) != 4)
                throw new BinkException("Unexpected end of file.");
            return (uint)(buffer[0] | buffer[1] << 8 | buffer[2] << 16 | buffer[3] << 24);
        }

        private ushort ReadUInt16()
        {
            Span<byte> buffer = stackalloc byte[2];
            if (_input.Read(buffer) != 2)
                throw new BinkException("Unexpected end of file.");
            return (ushort)(buffer[0] | buffer[1] << 8);
        }

        private void DecodeInit()
        {
            foreach (var frame in _frames)
                frame.SetSize(_width, _height);

            var blocks = ((_width + 7) >> 3) * ((_height + 7) >> 3);
            for (var i = 0; i < _srcCount; i++)
                _bundles[i] = new Bundle { Data = new byte[blocks * 64] };
            for (var i = 0; i < 16; i++)
                _colHigh[i] = new Tree();
        }

        private static int Log2(int value)
        {
            var result = 0;
            while ((value >>= 1) != 0)
                result++;
            return result;
        }

        private void InitLengths(int width, int blockWidth)
        {
            width = (width + 7) / 8 * 8;

            _bundles[_srcBlockTypes].Length = Log2((width >> 3) + 511) + 1;
            _bundles[_srcSubBlockTypes].Length = Log2((width >> 4) + 511) + 1;
            _bundles[_srcColors].Length = Log2(blockWidth * 64 + 511) + 1;
            _bundles[_srcIntraDc].Length =
                _bundles[_srcInterDc].Length =
                    _bundles[_srcXOff].Length =
                        _bundles[_srcYOff].Length = Log2((width >> 3) + 511) + 1;
            _bundles[_srcPattern].Length = Log2((blockWidth << 3) + 511) + 1;
            _bundles[_srcRun].Length = Log2(blockWidth * 48 + 511) + 1;
        }

        private void ParseFrame(int size)
        {
            var swapPlanes = _revision >= 'h';
            var bits = new BinkBitStream(_packet, size);

            if ((_flags & _flagAlpha) == _flagAlpha)
            {
                if (_revision >= 'i')
                    bits.Skip(32);
                DecodePlane(bits, 3, false);
            }
            if (_revision >= 'i')
                bits.Skip(32);

            for (var plane = 0; plane < 3; plane++)
            {
                var planeId = plane == 0 || !swapPlanes ? plane : plane ^ 3;
                if (_revision <= 'b')
                    throw new BinkException("Bink revision 'b' isn't supported.");

                DecodePlane(bits, planeId, plane != 0);
                if (bits.Position >= (long)size << 3)
                    break;
            }
        }

        private void DecodePlane(BinkBitStream bits, int planeId, bool isChroma)
        {
            var blockWidth = isChroma ? (_width + 15) >> 4 : (_width + 7) >> 3;
            var blockHeight = isChroma ? (_height + 15) >> 4 : (_height + 7) >> 3;
            var width = _width >> (isChroma ? 1 : 0);

            var plane = _frames[_frameCounter % 2].Planes[planeId];
            var last = _frames[(_frameCounter + 1) % 2].Planes[planeId];

            if (_revision == 'k' && bits.GetBit() != 0)
            {
                plane.Fill((byte)bits.GetBits(8));
                bits.Align32(); // next plane data starts at a 32-bit boundary
                return;
            }

            InitLengths(Math.Max(width, 8), blockWidth);
            for (var i = 0; i < _srcCount; i++)
                ReadBundle(bits, i);

            for (var by = 0; by < blockHeight; by++)
            {
                ReadBlockTypes(bits, _bundles[_srcBlockTypes]);
                ReadBlockTypes(bits, _bundles[_srcSubBlockTypes]);
                ReadColors(bits, _bundles[_srcColors]);
                ReadPatterns(bits, _bundles[_srcPattern]);
                ReadMotionValues(bits, _bundles[_srcXOff]);
                ReadMotionValues(bits, _bundles[_srcYOff]);
                ReadDcs(bits, _bundles[_srcIntraDc], _dcStartBits, 0);
                ReadDcs(bits, _bundles[_srcInterDc], _dcStartBits, 1);
                ReadRuns(bits, _bundles[_srcRun]);

                for (var bx = 0; bx < blockWidth; bx++)
                {
                    var type = (BlockType)GetValue(_srcBlockTypes);
                    // 16x16 block type on an odd line = part of the already decoded block.
                    if ((by & 1) != 0 && type == BlockType.Scaled)
                    {
                        bx++;
                        continue;
                    }

                    var isScaled = false;
                    if (type == BlockType.Scaled)
                    {
                        type = (BlockType)GetValue(_srcSubBlockTypes);
                        isScaled = true;
                    }

                    DecodeBlock(bits, type, isScaled, bx, by, last);

                    if (isScaled)
                    {
                        plane.PutScaledBlock(bx, by, _block);
                        bx++;
                    }
                    else
                    {
                        plane.PutBlock8x8(bx, by, _block);
                    }
                }
            }

            bits.Align32();
        }

        private void DecodeBlock(BinkBitStream bits, BlockType type, bool isScaled, int bx, int by, BinkFrame.Plane last)
        {
            var dst = _block;
            switch (type)
            {
                case BlockType.Skip:
                    last.GetBlock8x8(bx, by, dst);
                    break;
                case BlockType.Fill:
                    Array.Fill(dst, (byte)GetValue(_srcColors));
                    break;
                case BlockType.Residue:
                {
                    var xOffset = GetValue(_srcXOff);
                    var yOffset = GetValue(_srcYOff);
                    last.GetPixels8x8(bx * 8 + xOffset, by * 8 + yOffset, _previous);

                    Array.Clear(_residue, 0, 64);
                    var masks = (int)bits.GetBits(7);
                    ReadResidue(bits, _residue, masks);
                    for (var i = 0; i < 64; i++)
                        dst[i] = unchecked((byte)(_previous[i] + _residue[i]));
                    break;
                }
                case BlockType.Intra:
                {
                    Array.Clear(_dctBlock, 0, 64);
                    _dctBlock[0] = GetValue(_srcIntraDc);
                    var quantIndex = ReadDctCoeffs(bits, _dctBlock, out var coefCount);
                    UnquantizeDctCoeffs(_dctBlock, quantIndex * 64, IntraQuant, coefCount);
                    for (var i = 0; i < 8; i++)
                        IdctColumn(_temp, i, _dctBlock, i);
                    for (var i = 0; i < 8; i++)
                        IdctRowToBytes(dst, i * 8, _temp, i * 8);
                    break;
                }
                case BlockType.Inter:
                {
                    var xOffset = GetValue(_srcXOff);
                    var yOffset = GetValue(_srcYOff);
                    last.GetPixels8x8(bx * 8 + xOffset, by * 8 + yOffset, _previous);

                    Array.Clear(_dctBlock, 0, 64);
                    _dctBlock[0] = GetValue(_srcInterDc);
                    var quantIndex = ReadDctCoeffs(bits, _dctBlock, out var coefCount);
                    UnquantizeDctCoeffs(_dctBlock, quantIndex * 64, InterQuant, coefCount);
                    for (var i = 0; i < 8; i++)
                        IdctColumn(_temp, i, _dctBlock, i);
                    for (var i = 0; i < 8; i++)
                        IdctRowToInts(_dctBlock, i * 8, _temp, i * 8);
                    for (var i = 0; i < 64; i++)
                        dst[i] = unchecked((byte)(_previous[i] + _dctBlock[i]));
                    break;
                }
                case BlockType.Run:
                {
                    var scan = (int)bits.GetBits(4) * 64;
                    var i = 0;
                    do
                    {
                        var run = GetValue(_srcRun) + 1;
                        i += run;
                        if (i > 64)
                            throw new BinkException("Run went out of bounds.");
                        if (bits.GetBit() != 0)
                        {
                            var value = (byte)GetValue(_srcColors);
                            for (var j = 0; j < run; j++)
                                dst[Patterns[scan++]] = value;
                        }
                        else
                        {
                            for (var j = 0; j < run; j++)
                                dst[Patterns[scan++]] = (byte)GetValue(_srcColors);
                        }
                    } while (i < 63);
                    if (i == 63)
                        dst[Patterns[scan]] = (byte)GetValue(_srcColors);
                    break;
                }
                case BlockType.Motion:
                {
                    if (isScaled)
                        throw new BinkException("Unsupported type of superblock.");
                    var xOffset = GetValue(_srcXOff);
                    var yOffset = GetValue(_srcYOff);
                    last.GetPixels8x8(bx * 8 + xOffset, by * 8 + yOffset, dst);
                    break;
                }
                case BlockType.Pattern:
                {
                    var color0 = (byte)GetValue(_srcColors);
                    var color1 = (byte)GetValue(_srcColors);
                    for (var i = 0; i < 8; i++)
                    {
                        var value = GetValue(_srcPattern);
                        for (var j = 0; j < 8; j++, value >>= 1)
                            dst[i * 8 + j] = (value & 1) != 0 ? color1 : color0;
                    }
                    break;
                }
                case BlockType.Raw:
                {
                    var colors = _bundles[_srcColors];
                    Buffer.BlockCopy(colors.Data, colors.CurrentRead, dst, 0, 64);
                    colors.CurrentRead += 64;
                    break;
                }
                default:
                    throw new BinkException($"Block type {type} not implemented.");
            }
        }

        // ---------------------------------------------------------------------------------------------------------
        // Bundles: values are decoded per block row into the bundle buffers, then read by GetValue().
        // ---------------------------------------------------------------------------------------------------------

        private void ReadBundle(BinkBitStream bits, int bundleNumber)
        {
            if (bundleNumber == _srcColors)
            {
                for (var i = 0; i < 16; i++)
                    ReadTree(bits, _colHigh[i]);
                _colLastValue = 0;
            }

            if (bundleNumber != _srcIntraDc && bundleNumber != _srcInterDc)
                ReadTree(bits, _bundles[bundleNumber].Tree);

            _bundles[bundleNumber].CurrentDecode = 0;
            _bundles[bundleNumber].CurrentRead = 0;
        }

        private static void ReadTree(BinkBitStream bits, Tree tree)
        {
            tree.VlcNum = (int)bits.GetBits(4);
            if (tree.VlcNum == 0)
            {
                for (var i = 0; i < 16; i++)
                    tree.Symbols[i] = (byte)i;
                return;
            }

            if (bits.GetBit() != 0)
            {
                Span<bool> used = stackalloc bool[16];
                var length = (int)bits.GetBits(3);
                for (var i = 0; i <= length; i++)
                {
                    tree.Symbols[i] = (byte)bits.GetBits(4);
                    used[tree.Symbols[i]] = true;
                }
                for (var i = 0; i < 16 && length < 15; i++)
                {
                    if (!used[i])
                        tree.Symbols[++length] = (byte)i;
                }
            }
            else
            {
                var length = (int)bits.GetBits(2);
                var input = new byte[16];
                var output = new byte[16];
                for (var i = 0; i < 16; i++)
                    input[i] = (byte)i;
                for (var i = 0; i <= length; i++)
                {
                    var size = 1 << i;
                    for (var t = 0; t < 16; t += size << 1)
                        Merge(bits, output, t, input, t, size);
                    (input, output) = (output, input);
                }
                Buffer.BlockCopy(input, 0, tree.Symbols, 0, 16);
            }
        }

        private static void Merge(BinkBitStream bits, byte[] dst, int dstIndex, byte[] src, int srcIndex, int size)
        {
            var src2Index = srcIndex + size;
            var size2 = size;

            do
            {
                if (bits.GetBit() == 0)
                {
                    dst[dstIndex++] = src[srcIndex++];
                    size--;
                }
                else
                {
                    dst[dstIndex++] = src[src2Index++];
                    size2--;
                }
            } while (size != 0 && size2 != 0);

            while (size-- > 0)
                dst[dstIndex++] = src[srcIndex++];
            while (size2-- > 0)
                dst[dstIndex++] = src[src2Index++];
        }

        private static byte GetHuff(BinkBitStream bits, Tree tree)
        {
            var tableOffset = tree.VlcNum * 128;
            var index = (int)bits.ShowBits(_treeBits[tree.VlcNum]);
            var code = VlcTable[(tableOffset + index) * 2];
            var length = VlcTable[(tableOffset + index) * 2 + 1];
            bits.Skip(length);
            return tree.Symbols[code];
        }

        /// <summary>
        /// Reads the number of values for the current block row. False = nothing to decode (bundle done or count 0).
        /// </summary>
        private static bool ReadCount(BinkBitStream bits, Bundle bundle, out int count)
        {
            count = 0;
            if (bundle.CurrentDecode < 0 || bundle.CurrentDecode > bundle.CurrentRead)
                return false;

            count = (int)bits.GetBits(bundle.Length);
            if (count == 0)
            {
                bundle.CurrentDecode = -1;
                return false;
            }
            return true;
        }

        private void ReadBlockTypes(BinkBitStream bits, Bundle bundle)
        {
            if (!ReadCount(bits, bundle, out var count))
                return;

            if (_revision == 'k')
            {
                count ^= 0xBB;
                if (count == 0)
                {
                    bundle.CurrentDecode = -1;
                    return;
                }
            }

            var end = bundle.CurrentDecode + count;
            if (end > bundle.Data.Length)
                throw new BinkException("Too many block type values.");

            if (bits.GetBit() != 0)
            {
                var value = (byte)bits.GetBits(4);
                Array.Fill(bundle.Data, value, bundle.CurrentDecode, count);
                bundle.CurrentDecode += count;
                return;
            }

            var lastValue = 0;
            while (bundle.CurrentDecode < end)
            {
                int value = GetHuff(bits, bundle.Tree);
                if (value < 12)
                {
                    lastValue = value;
                    bundle.Data[bundle.CurrentDecode++] = (byte)value;
                }
                else
                {
                    var run = RleLens[value - 12];
                    if (end - bundle.CurrentDecode < run)
                        throw new BinkException("Decoding block error.");
                    Array.Fill(bundle.Data, (byte)lastValue, bundle.CurrentDecode, run);
                    bundle.CurrentDecode += run;
                }
            }
        }

        private int ReadColorValue(BinkBitStream bits, Bundle bundle)
        {
            _colLastValue = GetHuff(bits, _colHigh[_colLastValue]);
            var value = (_colLastValue << 4) | GetHuff(bits, bundle.Tree);
            if (_revision < 'i')
            {
                var sign = (sbyte)value >> 7;
                value = ((value & 0x7F) ^ sign) - sign;
                value += 0x80;
            }
            return value;
        }

        private void ReadColors(BinkBitStream bits, Bundle bundle)
        {
            if (!ReadCount(bits, bundle, out var count))
                return;

            var end = bundle.CurrentDecode + count;
            if (end > bundle.Data.Length)
                throw new BinkException("Too many color values.");

            if (bits.GetBit() != 0)
            {
                var value = ReadColorValue(bits, bundle);
                Array.Fill(bundle.Data, unchecked((byte)value), bundle.CurrentDecode, count);
                bundle.CurrentDecode += count;
                return;
            }

            while (bundle.CurrentDecode < end)
                bundle.Data[bundle.CurrentDecode++] = unchecked((byte)ReadColorValue(bits, bundle));
        }

        private static void ReadPatterns(BinkBitStream bits, Bundle bundle)
        {
            if (!ReadCount(bits, bundle, out var count))
                return;

            var end = bundle.CurrentDecode + count;
            if (end > bundle.Data.Length)
                throw new BinkException("Too many pattern values.");

            while (bundle.CurrentDecode < end)
            {
                var value = GetHuff(bits, bundle.Tree) | (GetHuff(bits, bundle.Tree) << 4);
                bundle.Data[bundle.CurrentDecode++] = (byte)value;
            }
        }

        private static void ReadMotionValues(BinkBitStream bits, Bundle bundle)
        {
            if (!ReadCount(bits, bundle, out var count))
                return;

            var end = bundle.CurrentDecode + count;
            if (end > bundle.Data.Length)
                throw new BinkException("Too many motion values.");

            if (bits.GetBit() != 0)
            {
                var value = (int)bits.GetBits(4);
                if (value != 0)
                {
                    var sign = -(int)bits.GetBit();
                    value = (value ^ sign) - sign;
                }
                Array.Fill(bundle.Data, unchecked((byte)value), bundle.CurrentDecode, count);
                bundle.CurrentDecode += count;
                return;
            }

            while (bundle.CurrentDecode < end)
            {
                int value = GetHuff(bits, bundle.Tree);
                if (value != 0)
                {
                    var sign = -(int)bits.GetBit();
                    value = (value ^ sign) - sign;
                }
                bundle.Data[bundle.CurrentDecode++] = unchecked((byte)value);
            }
        }

        /// <summary>
        /// DC values are 16 bit (stored little-endian in the byte bundle).
        /// </summary>
        private static void ReadDcs(BinkBitStream bits, Bundle bundle, int startBits, int hasSign)
        {
            if (!ReadCount(bits, bundle, out var length))
                return;

            var value = (int)bits.GetBits(startBits - hasSign);
            if (value != 0 && hasSign != 0)
            {
                var sign = -(int)bits.GetBit();
                value = (value ^ sign) - sign;
            }

            var dst = bundle.CurrentDecode;
            var dstEnd = bundle.Data.Length;
            if (dstEnd - dst < 2)
                throw new BinkException("io error");
            WriteInt16(bundle.Data, dst, value);
            dst += 2;
            length--;

            for (var i = 0; i < length; i += 8)
            {
                var length2 = Math.Min(length - i, 8);
                if ((dstEnd - dst) / 2 < length2)
                    throw new BinkException("io error");

                var bitSize = (int)bits.GetBits(4);
                if (bitSize > 0)
                {
                    for (var j = 0; j < length2; j++)
                    {
                        var delta = (int)bits.GetBits(bitSize);
                        if (delta != 0)
                        {
                            var sign = -(int)bits.GetBit();
                            delta = (delta ^ sign) - sign;
                        }
                        value += delta;
                        WriteInt16(bundle.Data, dst, value);
                        dst += 2;
                        if (value < -32768 || value > 32767)
                            throw new BinkException($"DC value went out of bounds: {value}");
                    }
                }
                else
                {
                    for (var j = 0; j < length2; j++)
                    {
                        WriteInt16(bundle.Data, dst, value);
                        dst += 2;
                    }
                }
            }

            bundle.CurrentDecode = dst;
        }

        private static void WriteInt16(byte[] data, int index, int value)
        {
            data[index] = unchecked((byte)value);
            data[index + 1] = unchecked((byte)(value >> 8));
        }

        private static void ReadRuns(BinkBitStream bits, Bundle bundle)
        {
            if (!ReadCount(bits, bundle, out var count))
                return;

            var end = bundle.CurrentDecode + count;
            if (end > bundle.Data.Length)
                throw new BinkException("Run value went out of bounds.");

            if (bits.GetBit() != 0)
            {
                Array.Fill(bundle.Data, (byte)bits.GetBits(4), bundle.CurrentDecode, count);
                bundle.CurrentDecode += count;
                return;
            }

            while (bundle.CurrentDecode < end)
                bundle.Data[bundle.CurrentDecode++] = GetHuff(bits, bundle.Tree);
        }

        /// <summary>
        /// Unsigned bytes for types/colors/patterns/runs, signed bytes for motion, signed 16 bit for DC values.
        /// </summary>
        private int GetValue(int source)
        {
            var bundle = _bundles[source];
            if (source < _srcXOff || source == _srcRun)
                return bundle.Data[bundle.CurrentRead++];
            if (source == _srcXOff || source == _srcYOff)
                return unchecked((sbyte)bundle.Data[bundle.CurrentRead++]);

            var value = (short)(bundle.Data[bundle.CurrentRead] | bundle.Data[bundle.CurrentRead + 1] << 8);
            bundle.CurrentRead += 2;
            return value;
        }

        // ---------------------------------------------------------------------------------------------------------
        // DCT coefficients, residue, IDCT
        // ---------------------------------------------------------------------------------------------------------

        private readonly int[] _coefList = new int[128];
        private readonly int[] _modeList = new int[128];

        private int ReadDctCoeffs(BinkBitStream bits, int[] block, out int coefCount)
        {
            var coefList = _coefList;
            var modeList = _modeList;
            Array.Clear(coefList, 0, 128);
            Array.Clear(modeList, 0, 128);
            var listStart = 64;
            var listEnd = 64;
            coefCount = 0;

            coefList[listEnd] = 4; modeList[listEnd++] = 0;
            coefList[listEnd] = 24; modeList[listEnd++] = 0;
            coefList[listEnd] = 44; modeList[listEnd++] = 0;
            coefList[listEnd] = 1; modeList[listEnd++] = 3;
            coefList[listEnd] = 2; modeList[listEnd++] = 3;
            coefList[listEnd] = 3; modeList[listEnd++] = 3;

            for (var bitCount = (int)bits.GetBits(4) - 1; bitCount >= 0; bitCount--)
            {
                var listPos = listStart;
                while (listPos < listEnd)
                {
                    if ((modeList[listPos] | coefList[listPos]) == 0 || bits.GetBit() == 0)
                    {
                        listPos++;
                        continue;
                    }

                    var coef = coefList[listPos];
                    var mode = modeList[listPos];
                    switch (mode)
                    {
                        case 0:
                        case 2:
                            if (mode == 0)
                            {
                                coefList[listPos] = coef + 4;
                                modeList[listPos] = 1;
                            }
                            else
                            {
                                coefList[listPos] = 0;
                                modeList[listPos++] = 0;
                            }
                            for (var i = 0; i < 4; i++, coef++)
                            {
                                if (bits.GetBit() != 0)
                                {
                                    coefList[--listStart] = coef;
                                    modeList[listStart] = 3;
                                }
                                else
                                {
                                    block[Scan[coef]] = ReadCoefficient(bits, bitCount);
                                    _coefIndex[coefCount++] = coef;
                                }
                            }
                            break;
                        case 1:
                            modeList[listPos] = 2;
                            for (var i = 0; i < 3; i++)
                            {
                                coef += 4;
                                coefList[listEnd] = coef;
                                modeList[listEnd++] = 2;
                            }
                            break;
                        case 3:
                            block[Scan[coef]] = ReadCoefficient(bits, bitCount);
                            _coefIndex[coefCount++] = coef;
                            coefList[listPos] = 0;
                            modeList[listPos++] = 0;
                            break;
                    }
                }
            }

            return (int)bits.GetBits(4); // quantizer index
        }

        private static int ReadCoefficient(BinkBitStream bits, int bitCount)
        {
            if (bitCount == 0)
                return 1 - (int)(bits.GetBit() << 1);

            var value = (int)bits.GetBits(bitCount) | 1 << bitCount;
            var sign = -(int)bits.GetBit();
            return (value ^ sign) - sign;
        }

        private void UnquantizeDctCoeffs(int[] block, int quantOffset, uint[] quant, int coefCount)
        {
            block[0] = unchecked((int)((uint)block[0] * quant[quantOffset])) >> 11;
            for (var i = 0; i < coefCount; i++)
            {
                var index = _coefIndex[i];
                block[Scan[index]] = unchecked((int)((uint)block[Scan[index]] * quant[quantOffset + index])) >> 11;
            }
        }

        private readonly int[] _nonZeroCoefs = new int[64];

        private void ReadResidue(BinkBitStream bits, short[] block, int masksCount)
        {
            var coefList = _coefList;
            var modeList = _modeList;
            var listStart = 64;
            var listEnd = 64;
            var nonZeroCount = 0;

            coefList[listEnd] = 4; modeList[listEnd++] = 0;
            coefList[listEnd] = 24; modeList[listEnd++] = 0;
            coefList[listEnd] = 44; modeList[listEnd++] = 0;
            coefList[listEnd] = 0; modeList[listEnd++] = 2;

            for (var mask = 1 << (int)bits.GetBits(3); mask != 0; mask >>= 1)
            {
                for (var i = 0; i < nonZeroCount; i++)
                {
                    if (bits.GetBit() == 0)
                        continue;
                    var index = _nonZeroCoefs[i];
                    block[index] = (short)(block[index] < 0 ? block[index] - mask : block[index] + mask);
                    if (--masksCount < 0)
                        return;
                }

                var listPos = listStart;
                while (listPos < listEnd)
                {
                    if ((coefList[listPos] | modeList[listPos]) == 0 || bits.GetBit() == 0)
                    {
                        listPos++;
                        continue;
                    }

                    var coef = coefList[listPos];
                    var mode = modeList[listPos];
                    switch (mode)
                    {
                        case 0:
                        case 2:
                            if (mode == 0)
                            {
                                coefList[listPos] = coef + 4;
                                modeList[listPos] = 1;
                            }
                            else
                            {
                                coefList[listPos] = 0;
                                modeList[listPos++] = 0;
                            }
                            for (var i = 0; i < 4; i++, coef++)
                            {
                                if (bits.GetBit() != 0)
                                {
                                    coefList[--listStart] = coef;
                                    modeList[listStart] = 3;
                                }
                                else
                                {
                                    _nonZeroCoefs[nonZeroCount++] = Scan[coef];
                                    var sign = -(int)bits.GetBit();
                                    block[Scan[coef]] = (short)((mask ^ sign) - sign);
                                    if (--masksCount < 0)
                                        return;
                                }
                            }
                            break;
                        case 1:
                            modeList[listPos] = 2;
                            for (var i = 0; i < 3; i++)
                            {
                                coef += 4;
                                coefList[listEnd] = coef;
                                modeList[listEnd++] = 2;
                            }
                            break;
                        case 3:
                        {
                            _nonZeroCoefs[nonZeroCount++] = Scan[coef];
                            var sign = -(int)bits.GetBit();
                            block[Scan[coef]] = (short)((mask ^ sign) - sign);
                            coefList[listPos] = 0;
                            modeList[listPos++] = 0;
                            if (--masksCount < 0)
                                return;
                            break;
                        }
                    }
                }
            }
        }

        private static int IdctMultiply(int x, int y) => unchecked((int)((uint)x * (uint)y)) >> 11;

        /// <summary>
        /// One 8-point IDCT pass. src/dst are read/written with the given stride (8 = column, 1 = row).
        /// </summary>
        private static void IdctTransform(int[] src, int srcOffset, int stride, Span<int> result)
        {
            const int a1 = 2896; // (1/sqrt(2))<<12
            const int a2 = 2217;
            const int a3 = 3784;
            const int a4 = -5352;

            int S(int i) => src[srcOffset + i * stride];

            var c0 = S(0) + S(4);
            var c1 = S(0) - S(4);
            var c2 = S(2) + S(6);
            var c3 = IdctMultiply(a1, S(2) - S(6));
            var c4 = S(5) + S(3);
            var c5 = S(5) - S(3);
            var c6 = S(1) + S(7);
            var c7 = S(1) - S(7);
            var b0 = c4 + c6;
            var b1 = IdctMultiply(a3, c5 + c7);
            var b2 = IdctMultiply(a4, c5) - b0 + b1;
            var b3 = IdctMultiply(a1, c6 - c4) - b2;
            var b4 = IdctMultiply(a2, c7) + b3 - b1;

            result[0] = c0 + c2 + b0;
            result[1] = c1 + c3 - c2 + b2;
            result[2] = c1 - c3 + c2 + b3;
            result[3] = c0 - c2 - b4;
            result[4] = c0 - c2 + b4;
            result[5] = c1 - c3 + c2 - b3;
            result[6] = c1 + c3 - c2 - b2;
            result[7] = c0 + c2 - b0;
        }

        private static void IdctColumn(int[] dst, int dstOffset, int[] src, int srcOffset)
        {
            // Only the DC value: the whole column is that value.
            if ((src[srcOffset + 8] | src[srcOffset + 16] | src[srcOffset + 24] | src[srcOffset + 32] |
                 src[srcOffset + 40] | src[srcOffset + 48] | src[srcOffset + 56]) == 0)
            {
                for (var i = 0; i < 8; i++)
                    dst[dstOffset + i * 8] = src[srcOffset];
                return;
            }

            Span<int> result = stackalloc int[8];
            IdctTransform(src, srcOffset, 8, result);
            for (var i = 0; i < 8; i++)
                dst[dstOffset + i * 8] = result[i];
        }

        private static void IdctRowToBytes(byte[] dst, int dstOffset, int[] src, int srcOffset)
        {
            Span<int> result = stackalloc int[8];
            IdctTransform(src, srcOffset, 1, result);
            for (var i = 0; i < 8; i++)
                dst[dstOffset + i] = unchecked((byte)((result[i] + 0x7F) >> 8));
        }

        private static void IdctRowToInts(int[] dst, int dstOffset, int[] src, int srcOffset)
        {
            Span<int> result = stackalloc int[8];
            IdctTransform(src, srcOffset, 1, result);
            for (var i = 0; i < 8; i++)
                dst[dstOffset + i] = (result[i] + 0x7F) >> 8;
        }
    }
}
