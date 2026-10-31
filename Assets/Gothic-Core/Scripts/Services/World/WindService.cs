using System.Collections.Generic;
using Gothic.Core.Logging;
using Gothic.Core.Services.Config;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.World
{
    /// <summary>
    /// Trees and grass sway in the wind like in the engine (OpenGothic VisualObjects::preFrameUpdateWind): VOBs with the
    /// visualAniMode WIND (trees) or WIND_ALT (grass, bushes) lean along the wind by their height and swing back and
    /// forth over Gothic.ini's wind cycle (zWindCycleTime + zWindCycleTimeVar seconds). Only G2's worlds have such VOBs.
    /// The swaying is done by the VOB shaders (GothicWind.hlsl) for the materials this service marks with the _WIND
    /// keyword - only while Gothic.ini's zWindEnabled is on, the in-game option. Off, the materials are the normal ones
    /// and the wind costs nothing: the option exists for Quest 2, where it starts off.
    /// </summary>
    public class WindService
    {
        public const string IniSection = "ENGINE";
        public const string IniKeyEnabled = "zWindEnabled";
        private const string _iniKeyCycleTime = "zWindCycleTime";
        private const string _iniKeyCycleTimeVar = "zWindCycleTimeVar";
        // OpenGothic's defaults (G2's Gothic.ini: 4 + 2).
        private const float _defaultCycleTime = 4f;
        private const float _defaultCycleTimeVar = 6f;

        private const string _keyword = "_WIND";
        private static readonly int _strengthId = Shader.PropertyToID("_WindStrength");
        private static readonly int _periodId = Shader.PropertyToID("_GothicWindPeriod");
        private static readonly int _directionId = Shader.PropertyToID("_GothicWindDirection");
        // Gothic's wind blows along the world's z axis.
        private static readonly Vector4 _direction = new(0f, 1f, 0f, 0f);

        // Lean per meter of height at the peak of the swing. Trees: OpenGothic's 0.03. Grass: its strength * 0.1.
        private const float _treeSwing = 0.03f;
        private const float _grassStrengthFactor = 0.1f;
        // Nearly all of G2's trees and plants have no strength of their own - like the engine's default they swing
        // like a tree, else they would stand still.
        private const float _defaultSwing = 0.03f;
        // Less isn't visible - such VOBs (strength 0.001) stay normal materials.
        private const float _minSwing = 0.002f;

        [Inject] private readonly ConfigService _configService;

        private readonly List<Material> _materials = new();
        private bool _isEnabled;
        private bool _isApplied;

        /// <summary>
        /// The option's value without a Gothic.ini entry: on, but off on the Quest (performance).
        /// </summary>
        public static int DefaultEnabled => Application.isMobilePlatform ? 0 : 1;

        public WindService()
        {
            GlobalEventDispatcher.PlayerPrefUpdated.AddListener(OnSettingChanged);
            GlobalEventDispatcher.LoadingSceneLoaded.AddListener(() => _materials.Clear());
        }

        /// <summary>
        /// The VOB's object was built: its materials sway if the VOB has wind.
        /// </summary>
        public void Register(IVirtualObject vob, GameObject go)
        {
            var swing = GetSwing(vob);
            if (swing < _minSwing)
                return;

            if (!_isApplied)
                ApplySettings();

            foreach (var meshRenderer in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                // The VOBs' materials are their own (StationaryLightsService sets per-object lights on them too).
                foreach (var material in meshRenderer.sharedMaterials)
                {
                    if (material == null || !material.HasProperty(_strengthId))
                        continue;

                    material.SetFloat(_strengthId, swing);
                    SetKeyword(material, _isEnabled);
                    _materials.Add(material);
                }
            }
        }

        /// <summary>
        /// OpenGothic: a tree sways if it has a strength, grass by its strength (up to 1), and a VOB without a mode but
        /// with a strength like grass.
        /// </summary>
        private static float GetSwing(IVirtualObject vob)
        {
            var strength = vob.AnimationStrength;
            switch (vob.AnimationType)
            {
                case AnimationType.Wind:
                    return _treeSwing;
                case AnimationType.WindAlt:
                    return strength > 0f ? Mathf.Min(strength, 1f) * _grassStrengthFactor : _defaultSwing;
                case AnimationType.None:
                    return strength > 0f && strength <= 1f ? strength * _grassStrengthFactor : 0f;
                default:
                    return 0f;
            }
        }

        private void OnSettingChanged(string key, object value)
        {
            if (key == IniKeyEnabled || key == _iniKeyCycleTime || key == _iniKeyCycleTimeVar)
                ApplySettings();
        }

        private void ApplySettings()
        {
            var ini = _configService.Gothic;
            if (ini == null)
                return;

            _isApplied = true;
            _isEnabled = ini.GetInt(IniKeyEnabled, DefaultEnabled) != 0;
            var period = ini.GetFloat(_iniKeyCycleTime, _defaultCycleTime) +
                         ini.GetFloat(_iniKeyCycleTimeVar, _defaultCycleTimeVar);
            Shader.SetGlobalFloat(_periodId, Mathf.Max(period, 0.5f));
            Shader.SetGlobalVector(_directionId, _direction);

            _materials.RemoveAll(material => material == null);
            foreach (var material in _materials)
                SetKeyword(material, _isEnabled);

            Logger.Log($"[Wind] {(_isEnabled ? "on" : "off")}, cycle {period:F1} s, {_materials.Count} materials.",
                LogCat.Vob);
        }

        private static void SetKeyword(Material material, bool isEnabled)
        {
            if (isEnabled)
                material.EnableKeyword(_keyword);
            else
                material.DisableKeyword(_keyword);
        }
    }
}
