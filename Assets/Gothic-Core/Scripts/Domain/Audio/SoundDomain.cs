using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GUZ.Core.Models.Audio;
using GUZ.Core.Services.Caches;
using JetBrains.Annotations;
using Reflex.Attributes;
using UnityEngine;

namespace GUZ.Core.Domain.Audio
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

        private ImaadpcmDecoderDomain _decoderDomain = new();


        public AudioClip CreateAudioClip(string fileName)
        {
            fileName = Path.GetFileNameWithoutExtension(fileName);

            if (_multiTypeCacheService.AudioClips.TryGetValue(fileName, out AudioClip cachedClip))
                return cachedClip;

            var soundBytes = _resourceCacheService.TryGetSoundBytes(fileName);
            if (soundBytes == null)
                return null;

            var soundData = ConvertWavByteArrayToFloatArray(soundBytes);

            var audioClip = AudioClip.Create(fileName, soundData.Sound.Length / soundData.Channels, soundData.Channels,
                soundData.SampleRate, false);
            audioClip.SetData(soundData.Sound, 0);

            _multiTypeCacheService.AudioClips.Add(fileName, audioClip);
            return audioClip;
        }

        public SoundModel ConvertWavByteArrayToFloatArray(byte[] fileBytes)
        {
            // HINT: Commented out elements are there for reference only.

            // string riffHeader = Encoding.ASCII.GetString(fileBytes, 0, 4);
            // int fileSize = BitConverter.ToInt32(fileBytes, 4);
            // string waveHeader = Encoding.ASCII.GetString(fileBytes, 8, 4);
            // string fmtHeader = Encoding.ASCII.GetString(fileBytes, 12, 4);
            // int fmtLength = BitConverter.ToInt32(fileBytes, 16);

            ushort formatType = BitConverter.ToUInt16(fileBytes, 20);
            string formatCode = FormatCode(formatType);
            ushort numChannels = BitConverter.ToUInt16(fileBytes, 22);
            int sampleRate = BitConverter.ToInt32(fileBytes, 24);

            // int byteRate = BitConverter.ToInt32(fileBytes, 28);
            // short blockAlign = BitConverter.ToInt16(fileBytes, 32);

            short bitsPerSample = BitConverter.ToInt16(fileBytes, 34);
            string dataHeader = Encoding.ASCII.GetString(fileBytes, 36, 4);

            // Check for "PAD" header and skip it if present
            int padSize = 0;
            while (dataHeader == "PAD ")
            {
                padSize += BitConverter.ToInt32(fileBytes, 40);

                // we add 8 bits to padding as to skip the pad subchunk header + data
                padSize += 8;

                // Skip the PAD section
                dataHeader = Encoding.ASCII.GetString(fileBytes, 36 + padSize, 4);
            }

            int dataSize = BitConverter.ToInt32(fileBytes, 40 + padSize);

            if (formatCode == "IMA ADPCM")
            {
                return ConvertWavByteArrayToFloatArray(_decoderDomain.Decode(fileBytes));
            }

            // sometimes a file has more data than is specified after the RIFF header
            long stopPosition = Math.Min(dataSize, (fileBytes.Length - 44));

            // Copy WAV data section into a new array
            var audioData = new byte[stopPosition];
            Array.Copy(fileBytes, 44+padSize, audioData, 0, stopPosition);

            return new SoundModel
            {
                Sound = ConvertByteArrayToFloatArray(audioData, 0, (BitDepth)bitsPerSample),
                Channels = numChannels,
                SampleRate = sampleRate
            };
        }

        private float[] ConvertByteArrayToFloatArray(byte[] source, int headerOffset, BitDepth bit)
        {
            switch (bit)
            {
                case BitDepth.Bit8:
                    {
                        var wavSize = BitConverter.ToInt32(source, headerOffset);

                        var data = new float[wavSize];

                        var maxValue = sbyte.MaxValue;

                        for (var i = 0; i < wavSize; i++)
                        {
                            data[i] = (float)source[i] / maxValue;
                        }

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
