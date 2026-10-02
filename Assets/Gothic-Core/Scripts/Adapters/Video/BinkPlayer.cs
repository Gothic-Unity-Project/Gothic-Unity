using System;
using System.Collections.Generic;
using System.IO;
using Gothic.Core.Bink;
using Gothic.Core.Logging;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Adapters.Video
{
    /// <summary>
    /// Plays an original Gothic Bink video (.bik) without any conversion: BinkVideo decodes raw YUV frames + PCM
    /// audio, this uploads the 3 planes into R8 textures ("Gothic/Bink YUV" converts to RGB on the GPU) and streams
    /// the audio into a streaming AudioClip. The picture follows the audio clock (or real time if silent).
    /// </summary>
    public class BinkPlayer : MonoBehaviour
    {
        private const int _framesAhead = 8;
        private const int _maxDecodesPerUpdate = 2;
        private const float _ringSeconds = 4f;

        private class DecodedFrame
        {
            public int Index;
            public readonly byte[][] Planes = new byte[3][];
        }

        private Stream _stream;
        private BinkVideo _video;
        private Action _onFinished;
        private AudioSource _audioSource;

        private readonly Texture2D[] _textures = new Texture2D[3];
        private readonly Queue<DecodedFrame> _decoded = new();
        private readonly Stack<DecodedFrame> _pool = new();
        private float _startTime;
        private bool _isFinished;

        // Audio ring buffer, read on the audio thread (OnAudioRead).
        private readonly object _audioLock = new();
        private float[] _ring;
        private int _ringRead;
        private int _ringCount;
        private long _samplesPlayed;
        private int _sampleRate;
        private int _channels;
        private bool _hasAudio;
        private bool _isAudioStarted;

        /// <summary>
        /// Starts the video on the screen renderer. onFinished is called at the end (also on decode errors).
        /// Returns false if the file can't be opened as Bink.
        /// </summary>
        public bool Play(string path, Renderer screen, AudioSource audioSource, Action onFinished, out float aspect)
        {
            aspect = 4f / 3f;
            _onFinished = onFinished;
            _audioSource = audioSource;

            try
            {
                _stream = new BufferedStream(File.OpenRead(path), 1 << 16);
                _video = new BinkVideo(_stream);
            }
            catch (Exception e)
            {
                Logger.LogWarning($"[BinkPlayer] Can't open '{path}': {e.Message}", LogCat.Loading);
                Close();
                return false;
            }

            aspect = (float)_video.Width / _video.Height;
            CreateTextures(screen);
            SetupAudio();

            Logger.Log($"[BinkPlayer] {Path.GetFileName(path)}: {_video.Width}x{_video.Height} @ {_video.Fps:F2} fps, " +
                       $"{_video.FrameCount} frames, audio={(_hasAudio ? $"{_sampleRate} Hz x{_channels}" : "none")}", LogCat.Loading);

            _startTime = Time.unscaledTime;
            return true;
        }

        public void Stop()
        {
            _isFinished = true;
            Close();
        }

        private void CreateTextures(Renderer screen)
        {
            var lumaStride = (_video.Width + 15) / 16 * 16;
            var lumaHeight = (_video.Height + 15) / 16 * 16;
            var chromaStride = (_video.Width / 2 + 15) / 16 * 16;
            var chromaHeight = (_video.Height / 2 + 15) / 16 * 16;

            _textures[0] = CreatePlaneTexture(lumaStride, lumaHeight);
            _textures[1] = CreatePlaneTexture(chromaStride, chromaHeight);
            _textures[2] = CreatePlaneTexture(chromaStride, chromaHeight);

            var material = new Material(Shader.Find("Gothic/Bink YUV"));
            material.SetTexture("_TexY", _textures[0]);
            material.SetTexture("_TexU", _textures[1]);
            material.SetTexture("_TexV", _textures[2]);
            material.SetVector("_ScaleY", new Vector4((float)_video.Width / lumaStride, (float)_video.Height / lumaHeight));
            material.SetVector("_ScaleUV",
                new Vector4((float)(_video.Width / 2) / chromaStride, (float)(_video.Height / 2) / chromaHeight));
            screen.sharedMaterial = material;
        }

        private static Texture2D CreatePlaneTexture(int width, int height)
        {
            return new Texture2D(width, height, TextureFormat.R8, false, true)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
        }

        private void SetupAudio()
        {
            if (_video.AudioTracks.Count == 0 || _audioSource == null)
                return;

            var track = _video.AudioTracks[0];
            _sampleRate = track.SampleRate;
            _channels = track.IsStereo ? 2 : 1;
            _ring = new float[(int)(_sampleRate * _channels * _ringSeconds)];
            _hasAudio = true;

            var lengthFrames = (int)Math.Ceiling(_video.FrameCount / _video.Fps * _sampleRate) + _sampleRate;
            var clip = AudioClip.Create("Bink", lengthFrames, _channels, _sampleRate, true, OnAudioRead);
            _audioSource.clip = clip;
            _audioSource.loop = false;
        }

        private void Update()
        {
            if (_video == null || _isFinished)
                return;

            try
            {
                Decode();
            }
            catch (Exception e)
            {
                Logger.LogWarning($"[BinkPlayer] Decoding stopped at frame {_video.CurrentFrame}: {e.Message}", LogCat.Loading);
                Finish();
                return;
            }

            // Start the sound once a few frames are buffered - the picture follows the sound from then on.
            if (_hasAudio && !_isAudioStarted && (_decoded.Count >= _framesAhead || IsDecodingDone))
            {
                _isAudioStarted = true;
                _startTime = Time.unscaledTime;
                _audioSource.Play();
            }

            ShowCurrentFrame();

            if (IsDecodingDone && _decoded.Count == 0 && GetPlaybackTime() * _video.Fps >= _video.FrameCount)
                Finish();
        }

        private bool IsDecodingDone => _video == null || _video.CurrentFrame >= _video.FrameCount;

        // Read on the audio thread - the decoder itself must not be touched there.
        private volatile bool _isDecodingDoneForAudio;

        private void Decode()
        {
            var limit = _isAudioStarted || !_hasAudio ? _maxDecodesPerUpdate : _framesAhead;
            for (var i = 0; i < limit && _decoded.Count < _framesAhead && !IsDecodingDone; i++)
            {
                var index = _video.CurrentFrame;
                var frame = _video.NextFrame();

                var decoded = _pool.Count > 0 ? _pool.Pop() : new DecodedFrame();
                decoded.Index = index;
                for (var plane = 0; plane < 3; plane++)
                {
                    var source = frame.Planes[plane].Data;
                    if (decoded.Planes[plane] == null || decoded.Planes[plane].Length != source.Length)
                        decoded.Planes[plane] = new byte[source.Length];
                    Buffer.BlockCopy(source, 0, decoded.Planes[plane], 0, source.Length);
                }
                _decoded.Enqueue(decoded);

                if (_hasAudio)
                    PushAudio(frame.Audio[0]);
            }
            _isDecodingDoneForAudio = IsDecodingDone;
        }

        private void ShowCurrentFrame()
        {
            if (_hasAudio && !_isAudioStarted)
                return;

            var targetIndex = (int)(GetPlaybackTime() * _video.Fps);
            DecodedFrame toShow = null;
            while (_decoded.Count > 0 && _decoded.Peek().Index <= targetIndex)
            {
                if (toShow != null)
                    _pool.Push(toShow);
                toShow = _decoded.Dequeue();
            }

            if (toShow == null)
                return;

            for (var plane = 0; plane < 3; plane++)
            {
                _textures[plane].LoadRawTextureData(toShow.Planes[plane]);
                _textures[plane].Apply(false);
            }
            _pool.Push(toShow);
        }

        private double GetPlaybackTime()
        {
            if (!_hasAudio)
                return Time.unscaledTime - _startTime;

            lock (_audioLock)
                return (double)_samplesPlayed / (_sampleRate * _channels);
        }

        private void PushAudio(List<float> samples)
        {
            lock (_audioLock)
            {
                foreach (var sample in samples)
                {
                    if (_ringCount == _ring.Length)
                        break; // buffer full - should not happen with a few frames ahead
                    _ring[(_ringRead + _ringCount) % _ring.Length] = sample;
                    _ringCount++;
                }
            }
        }

        /// <summary>
        /// Audio thread. Missing samples (decoder behind) are silence.
        /// </summary>
        private void OnAudioRead(float[] data)
        {
            lock (_audioLock)
            {
                for (var i = 0; i < data.Length; i++)
                {
                    if (_ringCount > 0)
                    {
                        data[i] = _ring[_ringRead];
                        _ringRead = (_ringRead + 1) % _ring.Length;
                        _ringCount--;
                        _samplesPlayed++;
                    }
                    else
                    {
                        data[i] = 0f;
                        // Keep the clock running at the end so the last frames are shown.
                        if (_isDecodingDoneForAudio)
                            _samplesPlayed++;
                    }
                }
            }
        }

        private void Finish()
        {
            if (_isFinished)
                return;
            _isFinished = true;
            Close();
            _onFinished?.Invoke();
        }

        private void Close()
        {
            if (_audioSource != null)
                _audioSource.Stop();
            _stream?.Dispose();
            _stream = null;
            _video = null;
            foreach (var texture in _textures)
            {
                if (texture != null)
                    Destroy(texture);
            }
        }

        private void OnDestroy()
        {
            Close();
        }
    }
}
