using Gothic.Core.Logging;
using Gothic.Core.Models.UI;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.UI
{
    /// <summary>
    /// Colors adjusted for color blindness (daltonization, Fidaner et al.): what a red-, green- or blue-weak eye can't
    /// tell apart (Machado et al. 2009, full severity) is moved into the colors it can. A global URP volume with a
    /// channel mixer does it - baked into the color grading table, so it costs only while a mode is on, when the camera's
    /// post-processing is switched on for it.
    /// </summary>
    public class ColorVisionService
    {
        private const int _volumePriority = 100;

        // How a weak eye sees RGB (Machado et al. 2009, severity 1).
        private static readonly Matrix4x4 _protanopia = Rows(
            0.152286f, 1.052583f, -0.204868f,
            0.114503f, 0.786281f, 0.099216f,
            -0.003882f, -0.048116f, 1.051998f);
        private static readonly Matrix4x4 _deuteranopia = Rows(
            0.367322f, 0.860646f, -0.227968f,
            0.280085f, 0.672501f, 0.047413f,
            -0.011820f, 0.042940f, 0.968881f);
        private static readonly Matrix4x4 _tritanopia = Rows(
            1.255528f, -0.076749f, -0.178779f,
            -0.078411f, 0.930809f, 0.147602f,
            0.004733f, 0.691367f, 0.303900f);

        // Where the lost difference goes: red/green differences into green and blue, blue differences into red and green.
        private static readonly Matrix4x4 _redGreenShift = Rows(
            0f, 0f, 0f,
            0.7f, 1f, 0f,
            0.7f, 0f, 1f);
        private static readonly Matrix4x4 _blueShift = Rows(
            1f, 0f, 0.7f,
            0f, 1f, 0.7f,
            0f, 0f, 0f);

        private Volume _volume;
        private ChannelMixer _channelMixer;
        private Camera _camera;
        private bool _hadPostProcessing;

        public ColorVisionMode Mode { get; private set; }

        /// <summary>
        /// Colors of this camera for the mode. The volume lives below the camera and goes with it.
        /// </summary>
        public void SetMode(ColorVisionMode mode, Camera camera)
        {
            if (camera == null)
                return;

            if (camera != _camera)
            {
                _camera = camera;
                _hadPostProcessing = GetCameraData(camera)?.renderPostProcessing ?? false;
                CreateVolume(camera);
            }

            Mode = mode;
            var isOn = mode != ColorVisionMode.Normal;
            if (isOn)
                ApplyCorrection(GetCorrection(mode));

            _volume.enabled = isOn;
            var cameraData = GetCameraData(camera);
            if (cameraData != null)
                cameraData.renderPostProcessing = isOn || _hadPostProcessing;

            Logger.Log($"[ColorVision] {mode}", LogCat.Misc);
        }

        /// <summary>
        /// Daltonization: color + shift x (color - what the weak eye sees).
        /// </summary>
        private static Matrix4x4 GetCorrection(ColorVisionMode mode)
        {
            var (simulation, shift) = mode switch
            {
                ColorVisionMode.Protanopia => (_protanopia, _redGreenShift),
                ColorVisionMode.Deuteranopia => (_deuteranopia, _redGreenShift),
                _ => (_tritanopia, _blueShift)
            };

            var lost = Subtract(Matrix4x4.identity, simulation);
            return Add(Matrix4x4.identity, shift * lost);
        }

        private void CreateVolume(Camera camera)
        {
            if (_volume != null)
                Object.Destroy(_volume.gameObject);

            var go = new GameObject("ColorVisionVolume");
            go.transform.SetParent(camera.transform, false);
            _volume = go.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = _volumePriority;
            _volume.profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _channelMixer = _volume.profile.Add<ChannelMixer>(true);
        }

        private void ApplyCorrection(Matrix4x4 correction)
        {
            // The mixer takes percentages: output channel = sum of input channels x percent / 100.
            _channelMixer.redOutRedIn.Override(correction.m00 * 100f);
            _channelMixer.redOutGreenIn.Override(correction.m01 * 100f);
            _channelMixer.redOutBlueIn.Override(correction.m02 * 100f);
            _channelMixer.greenOutRedIn.Override(correction.m10 * 100f);
            _channelMixer.greenOutGreenIn.Override(correction.m11 * 100f);
            _channelMixer.greenOutBlueIn.Override(correction.m12 * 100f);
            _channelMixer.blueOutRedIn.Override(correction.m20 * 100f);
            _channelMixer.blueOutGreenIn.Override(correction.m21 * 100f);
            _channelMixer.blueOutBlueIn.Override(correction.m22 * 100f);
        }

        private static UniversalAdditionalCameraData GetCameraData(Camera camera)
        {
            return camera.TryGetComponent<UniversalAdditionalCameraData>(out var data) ? data : null;
        }

        private static Matrix4x4 Rows(float m00, float m01, float m02, float m10, float m11, float m12, float m20,
            float m21, float m22)
        {
            var matrix = Matrix4x4.identity;
            matrix.SetRow(0, new Vector4(m00, m01, m02, 0f));
            matrix.SetRow(1, new Vector4(m10, m11, m12, 0f));
            matrix.SetRow(2, new Vector4(m20, m21, m22, 0f));
            return matrix;
        }

        private static Matrix4x4 Add(Matrix4x4 a, Matrix4x4 b)
        {
            var result = Matrix4x4.zero;
            for (var i = 0; i < 16; i++)
                result[i] = a[i] + b[i];
            return result;
        }

        private static Matrix4x4 Subtract(Matrix4x4 a, Matrix4x4 b)
        {
            var result = Matrix4x4.zero;
            for (var i = 0; i < 16; i++)
                result[i] = a[i] - b[i];
            return result;
        }
    }
}
