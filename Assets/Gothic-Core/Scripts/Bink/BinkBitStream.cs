// Bink video decoder - C# port of OpenGothic common/bink/video.cpp Video::BitStream (https://github.com/Try/OpenGothic),
// codec based on FFmpeg libavcodec/bink.c - licensed LGPL 2.1 or later. See README.md in this folder.
// Copyright (c) 2019 Try (OpenGothic, MIT). FFmpeg code: LGPL-2.1-or-later - copyright holders listed in README.md.
using System;

namespace Gothic.Core.Bink
{
    /// <summary>
    /// Little-endian bit reader (lowest bit first) of a Bink packet.
    /// C# port of OpenGothic common/bink/video.cpp Video::BitStream (codec based on FFmpeg - LGPL 2.1+).
    /// </summary>
    internal class BinkBitStream
    {
        private readonly byte[] _data;
        private readonly int _byteCount;
        private readonly long _bitCount;
        private long _at;

        public BinkBitStream(byte[] data, int byteCount)
        {
            _data = data;
            _byteCount = byteCount;
            _bitCount = (long)byteCount << 3;
        }

        public long Position => _at;
        public long BitsLeft => _bitCount - _at;

        public void Skip(long count)
        {
            if (_at + count > _bitCount)
                throw new BinkException("io error");
            _at += count;
        }

        public float GetFloat()
        {
            var power = (int)GetBits(5);
            var value = (float)(GetBits(23) * Math.Pow(2, power - 23));
            return GetBit() != 0 ? -value : value;
        }

        public int GetInt32() => unchecked((int)GetBits(32));

        public uint GetBit()
        {
            if (_at >= _bitCount)
                throw new BinkException("io error");
            var value = _data[_at >> 3];
            var mask = 1 << (int)(_at & 7);
            _at++;
            return (value & mask) != 0 ? 1u : 0u;
        }

        public uint GetBits(int count)
        {
            if (_at + count > _bitCount)
                throw new BinkException("io error");
            var value = Fetch32();
            _at += count;
            return count >= 32 ? value : value & ((1u << count) - 1);
        }

        public uint ShowBits(int count)
        {
            if (_at >= _bitCount)
                throw new BinkException("io error");
            var value = Fetch32();
            return count >= 32 ? value : value & ((1u << count) - 1);
        }

        public void Align32()
        {
            if ((_at & 0x1F) != 0)
                Skip(32 - (_at & 0x1F));
        }

        private uint Fetch32()
        {
            var byteAt = (int)(_at >> 3);
            var offset = (int)(_at & 7);

            ulong value = 0;
            for (var i = 0; i < 5; i++)
            {
                if (byteAt + i >= _byteCount)
                    break;
                value |= (ulong)_data[byteAt + i] << (8 * i);
            }
            return (uint)(value >> offset);
        }
    }

    public class BinkException : Exception
    {
        public BinkException(string message) : base(message)
        {
        }
    }
}
