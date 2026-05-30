using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using GUZ.Core.Extensions;
using GUZ.Core.Logging;
using GUZ.Core.Services;
using MyBox;
using Newtonsoft.Json;
using Reflex.Attributes;
using Unity.Collections;
using Unity.InferenceEngine;
using UnityEngine;
using Logger = GUZ.Core.Logging.Logger;

namespace GUZ.Core.Domain.Audio
{
    /// <summary>
    /// Implementation used from: https://huggingface.co/unity/inference-engine-whisper-tiny
    /// </summary>
    public class SpeechToTextDomain : IDisposable
    {
        [Inject] private readonly GameStateService _gameStateService;

        
        public bool IsInitialized;
        public bool IsTranscribing;
        public string OutputString;


        private Worker _decoder1, _decoder2, _encoder, _spectrogram;
        private Worker _argmax;

        private AudioClip _audioClip;

        // This is how many tokens you want. It can be adjusted.
        private const int _maxTokens = 100;

        // Whisper is sometimes stuck. We therefore enforce a stop after certain amount of time.
        private const float _forcedTimeout = 5f;

        // Special tokens see added tokens file for details
        private const int _endOfText = 50257;
        private const int _startOfTranscript = 50258;


        // https://huggingface.co/openai/whisper-tiny/blob/main/added_tokens.json

        private const int _cs = 50283; // <|cs|>
        private const int _de = 50261; // <|de|>
        private const int _en = 50259; // <|en|>
        private const int _es = 50262; // <|es|>
        private const int _fr = 50265; // <|fr|>
        private const int _it = 50274; // <|it|>
        private const int _pl = 50269; // <|pl|>
        private const int _ru = 50263; // <|ru|>

        private const int _transcribe = 50359; //for speech-to-text in specified language
        private const int _noTimeStamps = 50363;

        private int _numSamples;
        private string[] _tokens;

        private int _tokenCount;
        private NativeArray<int> _outputTokens;

        // Used for special character decoding
        private int[] _whiteSpaceCharacters = new int[256];

        private Tensor<float> _encodedAudio;

        // Maximum size of audioClip (30s at 16kHz)
        private const int _maxSamples = 30 * 16000;

        private float _runtime;

        public void Init()
        {
            try
            {
                // .onnx models need to be serialized. Prepared via Editor and stored as .sentis
                // @see: https://docs.unity3d.com/Packages/com.unity.ai.inference@2.2/manual/serialize-a-model.html
                var decoder1FilePath = $"{GetRootPath()}/decoder_model.sentis";
                var decoder2FilePath = $"{GetRootPath()}/decoder_with_past_model.sentis";
                var encoderFilePath = $"{GetRootPath()}/encoder_model.sentis";
                var logmelFilePath = $"{GetRootPath()}/logmel_spectrogram.sentis";
                var vocabFilePath = $"{GetRootPath()}/vocab.json";

                if (!File.Exists(decoder1FilePath) || !File.Exists(decoder2FilePath) || !File.Exists(encoderFilePath) ||
                    !File.Exists(logmelFilePath) || !File.Exists(vocabFilePath))
                {
                    Logger.Log("Whisper can't be initialized, as no LLM is found.", LogCat.Audio);
                    return;
                }

                var audioDecoder1 = ModelLoader.Load(decoder1FilePath);
                var audioDecoder2 = ModelLoader.Load(decoder2FilePath);
                var audioEncoder = ModelLoader.Load(encoderFilePath);
                var logMelSpectro = ModelLoader.Load(logmelFilePath);
                _vocabText = File.ReadAllText(vocabFilePath);

                if (audioDecoder1 == null || audioDecoder2 == null || audioEncoder == null || logMelSpectro == null ||
                    _vocabText.IsNullOrEmpty())
                {
                    Logger.Log("Whisper can't be initialized, as LLM couldn't be initialized.", LogCat.Audio);
                    return;
                }

                _decoder1 = new Worker(audioDecoder1, BackendType.GPUCompute);
                _decoder2 = new Worker(audioDecoder2, BackendType.GPUCompute);

                var graph = new FunctionalGraph();
                var input = graph.AddInput(DataType.Float, new DynamicTensorShape(1, 1, 51865));
                var amax = Functional.ArgMax(input, -1, false);
                var selectTokenModel = graph.Compile(amax);
                _argmax = new Worker(selectTokenModel, BackendType.GPUCompute);

                _encoder = new Worker(audioEncoder, BackendType.GPUCompute);
                _spectrogram = new Worker(logMelSpectro, BackendType.GPUCompute);

                _outputTokens = new NativeArray<int>(_maxTokens, Allocator.Persistent);

                _outputTokens[0] = _startOfTranscript;
                _outputTokens[1] = GetLanguageToken();
                _outputTokens[2] = _transcribe; //TRANSLATE;//
                //outputTokens[3] = NO_TIME_STAMPS;// START_TIME;//
                // _tokenCount = 3; --> Will be reset to 3 every time we call Whisper.

                SetupWhiteSpaceShifts();
                GetTokens();
            }
            catch (Exception e)
            {
                Logger.LogError($"Whisper can't be initialized: {e}", LogCat.Audio);
                return;
            }

            Logger.Log("Whisper successfully initialized.", LogCat.Audio);

            IsInitialized = true;
        }

        public async Task StartExec(AudioClip audioClip)
        {
            IsTranscribing = false;
            OutputString = string.Empty;
            _runtime = 0f;
            _tokenCount = 3;

            if (audioClip.samples > _maxSamples)
            {
                Logger.LogWarning($"Only 30 seconds of recording are supported. {audioClip.samples} provided", LogCat.Audio);
                return;
            }
            else if (audioClip.frequency != 16000)
            {
                Logger.LogWarning($"16kHz of recording supported only. But {audioClip.frequency} provided.", LogCat.Audio);
                return;
            }
            else if (audioClip.channels != 1)
            {
                Logger.LogWarning($"Only mono is supported. But {audioClip.channels} provided.", LogCat.Audio);
                return;
            }

            _audioClip = audioClip;
            LoadAudio();
            EncodeAudio();
            IsTranscribing = true;

            _tokensTensor = new Tensor<int>(new TensorShape(1, _maxTokens));
            _pinnedTensorData = ComputeTensorData.Pin(_tokensTensor);
            _tokensTensor.Reshape(new TensorShape(1, _tokenCount));
            _tokensTensor.dataOnBackend.Upload<int>(_outputTokens, _tokenCount);

            _lastToken = new NativeArray<int>(1, Allocator.Persistent); _lastToken[0] = _noTimeStamps;
            _lastTokenTensor = new Tensor<int>(new TensorShape(1, 1), new[] { _noTimeStamps });

            while (true)
            {
                if (!IsTranscribing || _tokenCount >= (_outputTokens.Length - 1))
                {
                    StopExec();
                    return;
                }

                await InferenceStep().AwaitAndLog();
            }
        }

        /// <summary>
        /// Dispose data after execution. Otherwise, we get errors about GPU memory leaks.
        /// </summary>
        private void StopExec()
        {
            IsTranscribing = false;

            _lastToken.Dispose();
            _lastTokenTensor.Dispose();
            _pinnedTensorData.Dispose();
            _tokensTensor.Dispose();
            _audioInput.Dispose();
        }

        private int GetLanguageToken()
        {
            Logger.Log($"Trying to set Whisper language to {_gameStateService.Language}", LogCat.Audio);
            switch (_gameStateService.Language)
            {
                case "cs":
                    return _cs;
                case "de":
                    return _de;
                case "en":
                    return _en;
                case "es":
                    return _es;
                case "fr":
                    return _fr;
                case "it":
                    return _it;
                case "pl":
                    return _pl;
                case "ru":
                    return _ru;
                default:
                    Logger.LogWarning("No usable language for Whisper found. Fallback to >en<", LogCat.Audio);
                    return _en;
            }
        }


        /// <summary>
        /// Return path of settings file based on target architecture.
        /// </summary>
        private string GetRootPath()
        {
            // https://docs.unity3d.com/ScriptReference/Application-persistentDataPath.html
            // Will be: /storage/emulated/<userid>/Android/data/<packagename>/files
            if (Application.platform == RuntimePlatform.Android)
            {
                return Application.persistentDataPath + "/SpeechToText";
            }

            // https://docs.unity3d.com/ScriptReference/Application-streamingAssetsPath.html
            // Will be:
            // 1. Editor: Assets\StreamingAssets\
            // 2. Standalone: Build\Gothic-UnZENity_Data\StreamingAssets\
            return Application.streamingAssetsPath + "/SpeechToText";
        }


        private Awaitable _awaitable;

        private NativeArray<int> _lastToken;
        private Tensor<int> _lastTokenTensor;
        private Tensor<int> _tokensTensor;
        private ComputeTensorData _pinnedTensorData;
        private Tensor<float> _audioInput;

        private void LoadAudio()
        {
            _numSamples = _audioClip.samples;
            var data = new float[_maxSamples];
            _numSamples = _maxSamples;
            _audioClip.GetData(data, 0);
            _audioInput = new Tensor<float>(new TensorShape(1, _numSamples), data);
        }

        private void EncodeAudio()
        {
            _spectrogram.Schedule(_audioInput);
            var logmel = _spectrogram.PeekOutput() as Tensor<float>;
            _encoder.Schedule(logmel);
            _encodedAudio = _encoder.PeekOutput() as Tensor<float>;
        }

        private async Task InferenceStep()
        {
            if (_runtime > _forcedTimeout)
            {
                Logger.LogWarning($"Whisper/Inference stuck. Force stop audio inference after {_forcedTimeout}s", LogCat.Audio);
                IsTranscribing = false;
                return;
            }
            _runtime += Time.deltaTime;

            _decoder1.SetInput("input_ids", _tokensTensor);
            _decoder1.SetInput("encoder_hidden_states", _encodedAudio);
            _decoder1.Schedule();

            var past_key_values_0_decoder_key = _decoder1.PeekOutput("present.0.decoder.key") as Tensor<float>;
            var past_key_values_0_decoder_value = _decoder1.PeekOutput("present.0.decoder.value") as Tensor<float>;
            var past_key_values_1_decoder_key = _decoder1.PeekOutput("present.1.decoder.key") as Tensor<float>;
            var past_key_values_1_decoder_value = _decoder1.PeekOutput("present.1.decoder.value") as Tensor<float>;
            var past_key_values_2_decoder_key = _decoder1.PeekOutput("present.2.decoder.key") as Tensor<float>;
            var past_key_values_2_decoder_value = _decoder1.PeekOutput("present.2.decoder.value") as Tensor<float>;
            var past_key_values_3_decoder_key = _decoder1.PeekOutput("present.3.decoder.key") as Tensor<float>;
            var past_key_values_3_decoder_value = _decoder1.PeekOutput("present.3.decoder.value") as Tensor<float>;

            var past_key_values_0_encoder_key = _decoder1.PeekOutput("present.0.encoder.key") as Tensor<float>;
            var past_key_values_0_encoder_value = _decoder1.PeekOutput("present.0.encoder.value") as Tensor<float>;
            var past_key_values_1_encoder_key = _decoder1.PeekOutput("present.1.encoder.key") as Tensor<float>;
            var past_key_values_1_encoder_value = _decoder1.PeekOutput("present.1.encoder.value") as Tensor<float>;
            var past_key_values_2_encoder_key = _decoder1.PeekOutput("present.2.encoder.key") as Tensor<float>;
            var past_key_values_2_encoder_value = _decoder1.PeekOutput("present.2.encoder.value") as Tensor<float>;
            var past_key_values_3_encoder_key = _decoder1.PeekOutput("present.3.encoder.key") as Tensor<float>;
            var past_key_values_3_encoder_value = _decoder1.PeekOutput("present.3.encoder.value") as Tensor<float>;

            _decoder2.SetInput("input_ids", _lastTokenTensor);
            _decoder2.SetInput("past_key_values.0.decoder.key", past_key_values_0_decoder_key);
            _decoder2.SetInput("past_key_values.0.decoder.value", past_key_values_0_decoder_value);
            _decoder2.SetInput("past_key_values.1.decoder.key", past_key_values_1_decoder_key);
            _decoder2.SetInput("past_key_values.1.decoder.value", past_key_values_1_decoder_value);
            _decoder2.SetInput("past_key_values.2.decoder.key", past_key_values_2_decoder_key);
            _decoder2.SetInput("past_key_values.2.decoder.value", past_key_values_2_decoder_value);
            _decoder2.SetInput("past_key_values.3.decoder.key", past_key_values_3_decoder_key);
            _decoder2.SetInput("past_key_values.3.decoder.value", past_key_values_3_decoder_value);

            _decoder2.SetInput("past_key_values.0.encoder.key", past_key_values_0_encoder_key);
            _decoder2.SetInput("past_key_values.0.encoder.value", past_key_values_0_encoder_value);
            _decoder2.SetInput("past_key_values.1.encoder.key", past_key_values_1_encoder_key);
            _decoder2.SetInput("past_key_values.1.encoder.value", past_key_values_1_encoder_value);
            _decoder2.SetInput("past_key_values.2.encoder.key", past_key_values_2_encoder_key);
            _decoder2.SetInput("past_key_values.2.encoder.value", past_key_values_2_encoder_value);
            _decoder2.SetInput("past_key_values.3.encoder.key", past_key_values_3_encoder_key);
            _decoder2.SetInput("past_key_values.3.encoder.value", past_key_values_3_encoder_value);

            _decoder2.Schedule();

            var logits = _decoder2.PeekOutput("logits") as Tensor<float>;
            _argmax.Schedule(logits);
            using var t_Token = await _argmax.PeekOutput().ReadbackAndCloneAsync() as Tensor<int>;
            var index = t_Token[0];

            _outputTokens[_tokenCount] = _lastToken[0];
            _lastToken[0] = index;
            _tokenCount++;
            _tokensTensor.Reshape(new TensorShape(1, _tokenCount));
            _tokensTensor.dataOnBackend.Upload(_outputTokens, _tokenCount);
            _lastTokenTensor.dataOnBackend.Upload(_lastToken, 1);

            if (index == _endOfText)
            {
                IsTranscribing = false;
            }
            else if (index < _tokens.Length)
            {
                OutputString += GetUnicodeText(_tokens[index]);
            }
        }

        // Tokenizer
        private string _vocabText;
        private void GetTokens()
        {
            var vocab = JsonConvert.DeserializeObject<Dictionary<string, int>>(_vocabText);
            _tokens = new string[vocab.Count];
            foreach (var item in vocab)
            {
                _tokens[item.Value] = item.Key;
            }
        }

        private string GetUnicodeText(string text)
        {
            var bytes = Encoding.GetEncoding("ISO-8859-1").GetBytes(ShiftCharacterDown(text));
            return Encoding.UTF8.GetString(bytes);
        }

        private string ShiftCharacterDown(string text)
        {
            string outText = "";
            foreach (char letter in text)
            {
                outText += ((int)letter <= 256) ? letter : (char)_whiteSpaceCharacters[(int)(letter - 256)];
            }
            return outText;
        }

        private void SetupWhiteSpaceShifts()
        {
            for (int i = 0, n = 0; i < 256; i++)
            {
                if (IsWhiteSpace((char)i)) _whiteSpaceCharacters[n++] = i;
            }
        }

        private bool IsWhiteSpace(char c)
        {
            return !(('!' <= c && c <= '~') || ('�' <= c && c <= '�') || ('�' <= c && c <= '�'));
        }

        public void Dispose()
        {
            // Dispose workers
            _decoder1?.Dispose();
            _decoder2?.Dispose();
            _encoder?.Dispose();
            _spectrogram?.Dispose();
            _argmax?.Dispose();

            if (_outputTokens.IsCreated)
                _outputTokens.Dispose();

            _encodedAudio?.Dispose();

            _decoder1 = null;
            _decoder2 = null;
            _encoder = null;
            _spectrogram = null;
            _argmax = null;
            _encodedAudio = null;
        }
    }
}
