using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Gothic.Core.Logging;
using Gothic.Core.Models.Audio;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using JetBrains.Annotations;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Audio
{
    public class SoundDomain
    {
        private enum BitDepth
        {
            Bit8 = 8,
            Bit16 = 16
        }

        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;
        [Inject] private readonly ConfigService _configService;

        private ImaadpcmDecoderDomain _decoderDomain = new();


        public AudioClip CreateAudioClip(string fileName)
        {
            fileName = Path.GetFileNameWithoutExtension(fileName);

            if (_multiTypeCacheService.AudioClips.TryGetValue(fileName, out AudioClip cachedClip))
                return cachedClip;

            var soundBytes = _resourceCacheService.TryGetSoundBytes(fileName);
            if (soundBytes == null)
                return null;

            SoundModel soundData;
            try
            {
                soundData = ConvertWavByteArrayToFloatArray(soundBytes);
            }
            catch (Exception e)
            {
                // Any audio decode failure (a mod's malformed/unsupported file) degrades to "no audio,
                // text-length fallback" (see Output.cs) instead of throwing uncaught mid-dialog and
                // silently killing everything after it in the caller (subtitles included) — that exact
                // failure shape is what made a New Balance dialogue NPC's lines play with zero audio
                // AND zero text, since the crash happened before the subtitle lookup ever ran.
                Logger.LogWarning($"Failed to decode audio clip '{fileName}': {e.Message}", LogCat.Audio);
                return null;
            }

            var audioClip = AudioClip.Create(fileName, soundData.Sound.Length / soundData.Channels, soundData.Channels,
                soundData.SampleRate, false);
            audioClip.SetData(soundData.Sound, 0);

            _multiTypeCacheService.AudioClips.Add(fileName, audioClip);
            return audioClip;
        }

        public SoundModel ConvertWavByteArrayToFloatArray(byte[] fileBytes)
        {
            // Some mods (e.g. New Balance's dubbing) ship Ogg Vorbis with a ".wav" extension slapped
            // on — sniff real content instead of trusting the name/extension. Vanilla Gothic never
            // ships this, so normal play always takes the RIFF/WAV path below unchanged.
            if (fileBytes.Length >= 4 && fileBytes[0] == 'O' && fileBytes[1] == 'g' && fileBytes[2] == 'g' && fileBytes[3] == 'S')
            {
                if (!_configService.EffectiveEnableOggAudio)
                {
                    throw new Exception("Ogg Vorbis audio detected but EnableOggAudio is disabled.");
                }

                return ConvertOggByteArrayToFloatArray(fileBytes);
            }

            // RIFF container: "RIFF"(4) + fileSize(4) + "WAVE"(4), then a sequence of chunks
            // ("fmt ", "data", and others like LIST/INFO/fact/PAD/JUNK/bext/cue we don't care about).
            // Walk chunks by their own declared size instead of assuming fixed byte offsets — some
            // mod-shipped WAVs (e.g. New Balance's dubbing) insert extra chunks or an extended fmt
            // chunk before "data", which made the old fixed-offset reads land on garbage bytes for
            // bitsPerSample (observed as nonsensical values like "115 bit depth").
            const int riffHeaderSize = 12;

            ushort formatType = 0;
            ushort numChannels = 0;
            int sampleRate = 0;
            short bitsPerSample = 0;
            int dataStart = -1;
            int dataSize = 0;

            var pos = riffHeaderSize;
            while (pos + 8 <= fileBytes.Length)
            {
                var chunkId = Encoding.ASCII.GetString(fileBytes, pos, 4);
                var chunkSize = BitConverter.ToInt32(fileBytes, pos + 4);
                var chunkDataStart = pos + 8;

                if (chunkId == "fmt ")
                {
                    formatType = BitConverter.ToUInt16(fileBytes, chunkDataStart);
                    numChannels = BitConverter.ToUInt16(fileBytes, chunkDataStart + 2);
                    sampleRate = BitConverter.ToInt32(fileBytes, chunkDataStart + 4);
                    bitsPerSample = BitConverter.ToInt16(fileBytes, chunkDataStart + 14);
                }
                else if (chunkId == "data")
                {
                    dataStart = chunkDataStart;
                    // Sometimes a file has more data than is specified after the RIFF header.
                    dataSize = (int)Math.Min(chunkSize, fileBytes.Length - chunkDataStart);
                }

                // Chunks are word-aligned: +1 pad byte if the declared size is odd.
                pos = chunkDataStart + chunkSize + (chunkSize % 2);
            }

            if (dataStart < 0)
            {
                throw new Exception("WAV file has no 'data' chunk.");
            }

            string formatCode = FormatCode(formatType);
            if (formatCode == "IMA ADPCM")
            {
                return ConvertWavByteArrayToFloatArray(_decoderDomain.Decode(fileBytes));
            }

            // Copy WAV data section into a new array
            var audioData = new byte[dataSize];
            Array.Copy(fileBytes, dataStart, audioData, 0, dataSize);

            return new SoundModel
            {
                Sound = ConvertByteArrayToFloatArray(audioData, 0, (BitDepth)bitsPerSample),
                Channels = numChannels,
                SampleRate = sampleRate
            };
        }

        private SoundModel ConvertOggByteArrayToFloatArray(byte[] fileBytes)
        {
            using var stream = new MemoryStream(fileBytes);
            using var vorbisReader = new NVorbis.VorbisReader(stream, false);

            var channels = vorbisReader.Channels;
            var sampleRate = vorbisReader.SampleRate;

            // Read in chunks until exhausted rather than pre-sizing from TotalSamples - keeps this
            // correct even if a file's declared sample count doesn't match what's actually decodable.
            var samples = new List<float>();
            var buffer = new float[channels * 4096];
            int samplesRead;
            while ((samplesRead = vorbisReader.ReadSamples(buffer, 0, buffer.Length)) > 0)
            {
                for (var i = 0; i < samplesRead; i++)
                {
                    samples.Add(buffer[i]);
                }
            }

            return new SoundModel
            {
                Sound = samples.ToArray(),
                Channels = (ushort)channels,
                SampleRate = sampleRate
            };
        }

        private float[] ConvertByteArrayToFloatArray(byte[] source, int headerOffset, BitDepth bit)
        {
            switch (bit)
            {
                case BitDepth.Bit8:
                    {
                        // 8-bit WAV samples are unsigned (0-255), silence = 128.
                        var sampleCount = source.Length - headerOffset;
                        var data = new float[sampleCount];
                        for (var i = 0; i < sampleCount; i++)
                            data[i] = (source[headerOffset + i] - 128) / 128f;
                        return data;
                    }
                case BitDepth.Bit16:
                    {
                        var bytesPerSample = sizeof(short); // block size = 2
                        var sampleCount = source.Length / bytesPerSample;

                        var data = new float[sampleCount];

                        var maxValue = short.MaxValue;

                        for (var i = 0; i < sampleCount; i++)
                        {
                            var offset = i * bytesPerSample;
                            var sample = BitConverter.ToInt16(source, offset);
                            var floatSample = (float)sample / maxValue;
                            data[i] = floatSample;
                        }

                        return data;
                    }
                default:
                    throw new Exception(bit + " bit depth is not supported.");
            }
        }

        private string FormatCode(ushort code)
        {
            switch (code)
            {
                case 1:
                    return "PCM";
                case 2:
                    return "ADPCM";
                case 3:
                    return "IEEE";
                case 7:
                    return "μ-law";
                case 17:
                    return "IMA ADPCM";
                case 65534:
                    return "WaveFormatExtensable";
                default:
                    return "";
            }
        }
    }
}
