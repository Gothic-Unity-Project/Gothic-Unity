// Bink audio decoder (RDFT/DCT variants).
// C# port of OpenGothic common/bink/video.cpp (https://github.com/Try/OpenGothic, MIT repository), whose codec is
// itself based on FFmpeg libavcodec/binkaudio.c + fft/rdft/dct - licensed LGPL 2.1 or later. See README.md here.
using System;
using System.Collections.Generic;
using static Gothic.Core.Bink.BinkTables;

namespace Gothic.Core.Bink
{
    public partial class BinkVideo
    {
        public struct AudioTrackInfo
        {
            public int SampleRate;
            public bool IsStereo;
        }

        private class AudioContext
        {
            public const int MaxChannels = 2;
            public const int BlockMaxSize = MaxChannels << 11;

            public readonly float[][] Samples = new float[MaxChannels][];
            public readonly float[][] Previous = { new float[BlockMaxSize / 16], new float[BlockMaxSize / 16] };
            public readonly bool IsDct;

            public uint SampleRate;
            public int ChannelCount;
            public float Root;
            public int FrameLength;
            public int OverlapLength;
            public int Bits;
            public readonly int[] Bands = new int[26];
            public int BandCount;
            public float[] CosTable;
            public int SinOffset; // tsin = CosTable + SinOffset
            public ushort[] RevTable;
            public float[] Csc2;
            public float[] FftBuffer; // complex interleaved
            public readonly float[] QuantTable = new float[96];
            public bool IsFirst = true;

            public AudioContext(uint sampleRate, int channels, bool isDct)
            {
                SampleRate = sampleRate;
                ChannelCount = channels;
                IsDct = isDct;
            }
        }

        private static readonly float _sqrtHalf = (float)Math.Sqrt(0.5);
        private static readonly float[][] _cosTables = new float[18][];

        public readonly List<AudioTrackInfo> AudioTracks = new();
        private readonly List<AudioContext> _audioTracks = new();

        private void DecodeAudioInit(AudioContext audio)
        {
            var frameLengthBits = audio.SampleRate < 22050 ? 9 : audio.SampleRate < 44100 ? 10 : 11;

            if (audio.ChannelCount < 1 || audio.ChannelCount > 2)
                throw new BinkException($"Invalid number of audio channels: {audio.ChannelCount}");

            if (!audio.IsDct)
            {
                // Audio is already interleaved for the RDFT format variant.
                audio.SampleRate *= (uint)audio.ChannelCount;
                if (_revision != 'b')
                    frameLengthBits += Log2(audio.ChannelCount);
                audio.ChannelCount = 1;
            }

            audio.FrameLength = 1 << frameLengthBits;
            audio.OverlapLength = audio.FrameLength / 16;
            audio.Bits = frameLengthBits;
            var sampleRateHalf = (audio.SampleRate + 1) / 2;

            audio.Root = audio.IsDct
                ? audio.FrameLength / (float)(Math.Sqrt(audio.FrameLength) * 32768.0)
                : 2f / (float)(Math.Sqrt(audio.FrameLength) * 32768.0);
            for (var i = 0; i < 96; i++)
                audio.QuantTable[i] = (float)Math.Exp(i * 0.15289164787221953823f) * audio.Root; // 0.0664/log10(e)

            var bandCount = 1;
            for (; bandCount < 25; bandCount++)
            {
                if (sampleRateHalf <= WmaCriticalFreqs[bandCount - 1])
                    break;
            }
            audio.BandCount = bandCount;

            audio.Bands[0] = 2;
            for (var i = 1; i < bandCount; i++)
                audio.Bands[i] = (int)(WmaCriticalFreqs[i - 1] * audio.FrameLength / sampleRateHalf) & ~1;
            audio.Bands[bandCount] = audio.FrameLength;

            for (var i = 0; i < 18; i++)
                InitCosTable(i);
            audio.CosTable = _cosTables[audio.Bits];
            audio.SinOffset = (1 << audio.Bits) >> 2;

            var fftBits = audio.Bits - 1;
            var complexCount = 1 << fftBits;
            audio.FftBuffer = new float[complexCount * 2];
            audio.RevTable = new ushort[complexCount];
            for (var i = 0; i < complexCount; i++)
            {
                var k = -SplitRadixPermutation(i, complexCount, audio.IsDct) & (complexCount - 1);
                audio.RevTable[k] = (ushort)i;
            }

            audio.Csc2 = new float[complexCount];
            for (var i = 0; i < complexCount; i++)
                audio.Csc2[i] = 0.5f / (float)Math.Sin(Math.PI / (4 * complexCount) * (2 * i + 1));

            for (var channel = 0; channel < AudioContext.MaxChannels; channel++)
                audio.Samples[channel] = new float[audio.FrameLength];
        }

        private static int SplitRadixPermutation(int i, int n, bool inverse)
        {
            if (n <= 2)
                return i & 1;
            var m = n >> 1;
            if ((i & m) == 0)
                return SplitRadixPermutation(i, m, inverse) * 2;
            m >>= 1;
            if (inverse == ((i & m) == 0))
                return SplitRadixPermutation(i, m, inverse) * 4 + 1;
            return SplitRadixPermutation(i, m, inverse) * 4 - 1;
        }

        private static void InitCosTable(int index)
        {
            if (_cosTables[index] != null)
                return;

            var m = 1 << index;
            var frequency = 2 * Math.PI / m;
            var table = new float[m];
            for (var i = 0; i <= m / 4 && i < m; i++)
                table[i] = (float)Math.Cos(i * frequency);
            for (var i = 1; i < m / 4; i++)
                table[m / 2 - i] = table[i];
            _cosTables[index] = table;
        }

        private void ParseAudio(int size, int trackId, List<float> output)
        {
            var bits = new BinkBitStream(_packet, size);
            bits.Skip(32); // reported size

            var audio = _audioTracks[trackId];
            output.Clear();

            while (true)
            {
                ParseAudioBlock(bits, audio);

                var count = audio.FrameLength - audio.OverlapLength;
                if (audio.ChannelCount == 1)
                {
                    var samples = audio.Samples[0];
                    for (var i = 0; i < count; i++)
                        output.Add(samples[i]);
                }
                else
                {
                    var left = audio.Samples[0];
                    var right = audio.Samples[1];
                    for (var i = 0; i < count; i++)
                    {
                        output.Add(left[i]);
                        output.Add(right[i]);
                    }
                }

                bits.Align32();
                if (bits.BitsLeft == 0)
                    break;
            }
        }

        private void ParseAudioBlock(BinkBitStream bits, AudioContext audio)
        {
            Span<float> quant = stackalloc float[25];

            if (audio.IsDct)
                bits.Skip(2);

            for (var channel = 0; channel < audio.ChannelCount; channel++)
            {
                var coeffs = audio.Samples[channel];
                if (_revision == 'b')
                {
                    coeffs[0] = BitConverter.Int32BitsToSingle(bits.GetInt32()) * audio.Root;
                    coeffs[1] = BitConverter.Int32BitsToSingle(bits.GetInt32()) * audio.Root;
                }
                else
                {
                    coeffs[0] = bits.GetFloat() * audio.Root;
                    coeffs[1] = bits.GetFloat() * audio.Root;
                }

                for (var i = 0; i < audio.BandCount; i++)
                    quant[i] = audio.QuantTable[Math.Min((int)bits.GetBits(8), 95)];

                var k = 0;
                var q = quant[0];

                var index = 2;
                while (index < audio.FrameLength)
                {
                    int end;
                    if (_revision == 'b')
                        end = index + 16;
                    else if (bits.GetBit() != 0)
                        end = index + RleLengthTab[bits.GetBits(4)] * 8;
                    else
                        end = index + 8;
                    end = Math.Min(end, audio.FrameLength);

                    var width = (int)bits.GetBits(4);
                    if (width == 0)
                    {
                        Array.Clear(coeffs, index, end - index);
                        index = end;
                        while (audio.Bands[k] < index)
                            q = quant[k++];
                    }
                    else
                    {
                        while (index < end)
                        {
                            if (audio.Bands[k] == index)
                                q = quant[k++];
                            var coeff = bits.GetBits(width);
                            if (coeff != 0)
                                coeffs[index] = bits.GetBit() != 0 ? -q * coeff : q * coeff;
                            else
                                coeffs[index] = 0f;
                            index++;
                        }
                    }
                }

                if (audio.IsDct)
                {
                    coeffs[0] /= 0.5f;
                    DctCalc3(audio, coeffs);
                }
                else
                {
                    RdftCalc(audio, coeffs, false);
                }
            }

            // Cross-fade the overlap with the previous block.
            for (var channel = 0; channel < audio.ChannelCount; channel++)
            {
                var count = audio.OverlapLength * audio.ChannelCount;
                var samples = audio.Samples[channel];
                var previous = audio.Previous[channel];

                if (!audio.IsFirst)
                {
                    var j = channel;
                    for (var i = 0; i < audio.OverlapLength; i++, j += audio.ChannelCount)
                        samples[i] = (previous[i] * (count - j) + samples[i] * j) / count;
                }
                Array.Copy(samples, audio.FrameLength - audio.OverlapLength, previous, 0, audio.OverlapLength);
            }

            audio.IsFirst = false;
        }

        private static void DctCalc3(AudioContext audio, float[] data)
        {
            var n = 1 << audio.Bits;
            var next = data[n - 1];
            var inverseN = 1f / n;
            var costab = _cosTables[audio.Bits + 2];

            for (var i = n - 2; i >= 2; i -= 2)
            {
                var value1 = data[i];
                var value2 = data[i - 1] - data[i + 1];
                var c = costab[i];
                var s = costab[n - i];
                data[i] = c * value1 + s * value2;
                data[i + 1] = s * value1 - c * value2;
            }

            data[1] = 2 * next;
            RdftCalc(audio, data, true);

            for (var i = 0; i < n / 2; i++)
            {
                var tmp1 = data[i] * inverseN;
                var tmp2 = data[n - i - 1] * inverseN;
                var csc = audio.Csc2[i] * (tmp1 - tmp2);
                tmp1 += tmp2;
                data[i] = tmp1 + csc;
                data[n - i - 1] = tmp1 - csc;
            }
        }

        private static void RdftCalc(AudioContext audio, float[] data, bool isNegativeSign)
        {
            var n = 1 << audio.Bits;
            const float k1 = 0.5f;
            const float k2 = -0.5f;
            var tcos = audio.CosTable;
            var tsin = audio.SinOffset;
            var sign = isNegativeSign ? -1f : 1f;
            var signInverse = -sign;

            // i = 0 is special: the DC term is real, the N/2 term (also real) is packed with it.
            var evRe = data[0];
            data[0] = evRe + data[1];
            data[1] = evRe - data[1];

            int i;
            for (i = 1; i < n >> 2; i++)
            {
                var i1 = 2 * i;
                var i2 = n - i1;
                var eRe = k1 * (data[i1] + data[i2]);
                var oIm = k2 * (data[i2] - data[i1]);
                var eIm = k1 * (data[i1 + 1] - data[i2 + 1]);
                var oRe = k2 * (data[i1 + 1] + data[i2 + 1]);
                var sumRe = oRe * tcos[i] + sign * oIm * tcos[tsin + i];
                var sumIm = oIm * tcos[i] + signInverse * oRe * tcos[tsin + i];
                data[i1] = eRe + sumRe;
                data[i1 + 1] = eIm + sumIm;
                data[i2] = eRe - sumRe;
                data[i2 + 1] = sumIm - eIm;
            }

            data[0] *= k1;
            data[1] *= k1;
            data[2 * i + 1] = sign * data[2 * i + 1];

            FftPermute(audio, data);
            Fft(data, 0, 1 << (audio.Bits - 1), audio.Bits - 1);
        }

        private static void FftPermute(AudioContext audio, float[] z)
        {
            var count = 1 << (audio.Bits - 1);
            var buffer = audio.FftBuffer;
            for (var j = 0; j < count; j++)
            {
                var target = audio.RevTable[j] * 2;
                buffer[target] = z[j * 2];
                buffer[target + 1] = z[j * 2 + 1];
            }
            Array.Copy(buffer, z, count * 2);
        }

        // --- Split-radix FFT on interleaved complex floats (z[2k] = re, z[2k+1] = im), offsets in complex units ---

        private static void Fft(float[] z, int offset, int n, int order)
        {
            switch (n)
            {
                case 4:
                    Fft4(z, offset);
                    return;
                case 8:
                    Fft8(z, offset);
                    return;
                case 16:
                    Fft16(z, offset);
                    return;
            }

            Fft(z, offset, n / 2, order - 1);
            Fft(z, offset + n / 4 * 2, n / 4, order - 2);
            Fft(z, offset + n / 4 * 3, n / 4, order - 2);
            FftPass(z, offset, _cosTables[order], n / 4 / 2);
        }

        private static void Fft4(float[] z, int o)
        {
            float Re(int i) => z[(o + i) * 2];
            float Im(int i) => z[(o + i) * 2 + 1];

            var t3 = Re(0) - Re(1);
            var t1 = Re(0) + Re(1);
            var t8 = Re(3) - Re(2);
            var t6 = Re(3) + Re(2);
            z[(o + 2) * 2] = t1 - t6;
            z[(o + 0) * 2] = t1 + t6;
            var t4 = Im(0) - Im(1);
            var t2 = Im(0) + Im(1);
            var t7 = Im(2) - Im(3);
            var t5 = Im(2) + Im(3);
            z[(o + 3) * 2 + 1] = t4 - t8;
            z[(o + 1) * 2 + 1] = t4 + t8;
            z[(o + 3) * 2] = t3 - t7;
            z[(o + 1) * 2] = t3 + t7;
            z[(o + 2) * 2 + 1] = t2 - t5;
            z[(o + 0) * 2 + 1] = t2 + t5;
        }

        private static void Fft8(float[] z, int o)
        {
            Fft4(z, o);

            var z4Re = z[(o + 4) * 2]; var z5Re = z[(o + 5) * 2];
            var z4Im = z[(o + 4) * 2 + 1]; var z5Im = z[(o + 5) * 2 + 1];
            var z6Re = z[(o + 6) * 2]; var z7Re = z[(o + 7) * 2];
            var z6Im = z[(o + 6) * 2 + 1]; var z7Im = z[(o + 7) * 2 + 1];

            var t1 = z4Re + z5Re; z[(o + 5) * 2] = z4Re - z5Re;
            var t2 = z4Im + z5Im; z[(o + 5) * 2 + 1] = z4Im - z5Im;
            var t5 = z6Re + z7Re; z[(o + 7) * 2] = z6Re - z7Re;
            var t6 = z6Im + z7Im; z[(o + 7) * 2 + 1] = z6Im - z7Im;

            Butterflies(z, o, o + 2, o + 4, o + 6, t1, t2, t5, t6);
            Transform(z, o + 1, o + 3, o + 5, o + 7, _sqrtHalf, _sqrtHalf);
        }

        private static void Fft16(float[] z, int o)
        {
            var cos161 = _cosTables[4][1];
            var cos163 = _cosTables[4][3];

            Fft8(z, o);
            Fft4(z, o + 8);
            Fft4(z, o + 12);

            TransformZero(z, o, o + 4, o + 8, o + 12);
            Transform(z, o + 2, o + 6, o + 10, o + 14, _sqrtHalf, _sqrtHalf);
            Transform(z, o + 1, o + 5, o + 9, o + 13, cos161, cos163);
            Transform(z, o + 3, o + 7, o + 11, o + 15, cos163, cos161);
        }

        private static void FftPass(float[] z, int o, float[] cosTable, int n)
        {
            var o1 = 2 * n;
            var o2 = 4 * n;
            var o3 = 6 * n;
            var wre = 0;
            var wim = o1; // wim = wre + o1, walks backwards
            n--;

            TransformZero(z, o, o + o1, o + o2, o + o3);
            Transform(z, o + 1, o + o1 + 1, o + o2 + 1, o + o3 + 1, cosTable[wre + 1], cosTable[wim - 1]);
            do
            {
                o += 2;
                wre += 2;
                wim -= 2;
                Transform(z, o, o + o1, o + o2, o + o3, cosTable[wre], cosTable[wim]);
                Transform(z, o + 1, o + o1 + 1, o + o2 + 1, o + o3 + 1, cosTable[wre + 1], cosTable[wim - 1]);
            } while (--n > 0);
        }

        private static void Transform(float[] z, int a0, int a1, int a2, int a3, float wre, float wim)
        {
            var a2Re = z[a2 * 2]; var a2Im = z[a2 * 2 + 1];
            var a3Re = z[a3 * 2]; var a3Im = z[a3 * 2 + 1];
            var t1 = a2Re * wre + a2Im * wim;
            var t2 = -a2Re * wim + a2Im * wre;
            var t5 = a3Re * wre - a3Im * wim;
            var t6 = a3Re * wim + a3Im * wre;
            Butterflies(z, a0, a1, a2, a3, t1, t2, t5, t6);
        }

        private static void TransformZero(float[] z, int a0, int a1, int a2, int a3)
        {
            Butterflies(z, a0, a1, a2, a3, z[a2 * 2], z[a2 * 2 + 1], z[a3 * 2], z[a3 * 2 + 1]);
        }

        private static void Butterflies(float[] z, int a0, int a1, int a2, int a3, float t1, float t2, float t5, float t6)
        {
            var t3 = t5 - t1;
            t5 += t1;
            var a0Re = z[a0 * 2]; var a0Im = z[a0 * 2 + 1];
            var a1Re = z[a1 * 2]; var a1Im = z[a1 * 2 + 1];

            z[a2 * 2] = a0Re - t5;
            z[a0 * 2] = a0Re + t5;
            z[a3 * 2 + 1] = a1Im - t3;
            z[a1 * 2 + 1] = a1Im + t3;
            var t4 = t2 - t6;
            t6 = t2 + t6;
            z[a3 * 2] = a1Re - t4;
            z[a1 * 2] = a1Re + t4;
            z[a2 * 2 + 1] = a0Im - t6;
            z[a0 * 2 + 1] = a0Im + t6;
        }
    }
}
