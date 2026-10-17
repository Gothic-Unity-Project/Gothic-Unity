// Bink video decoder - C# port of OpenGothic common/bink/frame.h/.cpp (https://github.com/Try/OpenGothic),
// codec based on FFmpeg libavcodec/bink.c - licensed LGPL 2.1 or later. See README.md in this folder.
// Copyright (c) 2019 Try (OpenGothic, MIT). FFmpeg code: LGPL-2.1-or-later - copyright holders listed in README.md.
using System;
using System.Collections.Generic;

namespace Gothic.Core.Bink
{
    /// <summary>
    /// One decoded Bink frame: YUV420 planes (0 = Y, 1 = U, 2 = V, 3 = alpha) + decoded audio per track.
    /// C# port of OpenGothic common/bink/frame.h/.cpp (MIT repository, codec based on FFmpeg - LGPL 2.1+).
    /// </summary>
    public class BinkFrame
    {
        public class Plane
        {
            public int Width;
            public int Height;
            // Aligned to 16 (largest block size).
            public int Stride;
            public int AlignedHeight;
            public byte[] Data = Array.Empty<byte>();

            public void SetSize(int width, int height)
            {
                Stride = (width + 15) / 16 * 16;
                AlignedHeight = (height + 15) / 16 * 16;
                Data = new byte[Stride * AlignedHeight];
                Width = width;
                Height = height;
            }

            public void GetPixels8x8(int x, int y, byte[] output)
            {
                for (var row = 0; row < 8; row++)
                {
                    var src = x + (y + row) * Stride;
                    for (var col = 0; col < 8; col++)
                    {
                        var index = src + col;
                        // Motion vectors can point slightly outside in broken streams - read 0 instead of crashing.
                        output[col + row * 8] = (uint)index < (uint)Data.Length ? Data[index] : (byte)0;
                    }
                }
            }

            public void GetBlock8x8(int blockX, int blockY, byte[] output) => GetPixels8x8(blockX * 8, blockY * 8, output);

            public void PutBlock8x8(int blockX, int blockY, byte[] input)
            {
                for (var row = 0; row < 8; row++)
                    Buffer.BlockCopy(input, row * 8, Data, blockX * 8 + (row + blockY * 8) * Stride, 8);
            }

            public void PutScaledBlock(int blockX, int blockY, byte[] input)
            {
                for (var row = 0; row < 16; row++)
                {
                    var dst = blockX * 8 + (row + blockY * 8) * Stride;
                    var srcRow = row / 2 * 8;
                    for (var col = 0; col < 16; col++)
                        Data[dst + col] = input[col / 2 + srcRow];
                }
            }

            public void Fill(byte value) => Array.Fill(Data, value);
        }

        public readonly Plane[] Planes = { new(), new(), new(), new() };

        // Interleaved samples (stereo: L R L R ...) per audio track, decoded together with this frame.
        public readonly List<List<float>> Audio = new();

        public int Width => Planes[0].Width;
        public int Height => Planes[0].Height;

        public void SetSize(int width, int height)
        {
            Planes[0].SetSize(width, height);
            Planes[1].SetSize(width / 2, height / 2);
            Planes[2].SetSize(width / 2, height / 2);
            Planes[3].SetSize(width, height);
        }

        public void SetAudioChannels(int count)
        {
            Audio.Clear();
            for (var i = 0; i < count; i++)
                Audio.Add(new List<float>(4096));
        }
    }
}
