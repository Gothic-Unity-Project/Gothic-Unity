using System.Collections;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.World;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Npc
{
    /// <summary>
    /// The Light spell. Like in Gothic it has no script effect: the engine's visual effect (spellFX_Light_ACTIVE) is a
    /// white light (JUSTWHITE, lightRange 1000 cm) that follows the caster 1.2 m above the head. It lights the world in
    /// caves and houses through the world light pool and NPCs and items through a Unity light. Casting it again starts
    /// its time anew.
    /// </summary>
    public class LightSpellService
    {
        public const string SpellName = "Light";

        private const float _range = 10f;
        private const float _heightAboveHead = 1.2f;
        // Not in the scripts: the engine keeps the effect for a few minutes.
        private const float _durationSeconds = 300f;
        // The light follows the caster this often and once it moved this far (m) - each move re-gathers the renderers.
        private const float _followSeconds = 0.3f;
        private const float _minMove = 0.5f;

        [Inject] private readonly StationaryLightsService _stationaryLightsService;

        private GameObject _light;
        private float _endTime;

        public LightSpellService()
        {
            // A new world drops the light with the old scene.
            GlobalEventDispatcher.LoadingSceneLoaded.AddListener(() => _light = null);
        }

        public void Cast(NpcContainer caster)
        {
            if (caster?.PrefabProps == null)
                return;

            _endTime = Time.time + _durationSeconds;
            if (_light != null)
                return;

            _light = _stationaryLightsService.CreateMovingLight("Light spell", GetLightPosition(caster), _range,
                Color.white);

            caster.PrefabProps.StartCoroutine(FollowCaster(caster, _light));
            Logger.Log($"[LightSpell] {caster.Instance.GetName(ZenKit.Daedalus.NpcNameSlot.Slot0)} casts light.",
                LogCat.Fight);
        }

        private IEnumerator FollowCaster(NpcContainer caster, GameObject light)
        {
            var wait = new WaitForSeconds(_followSeconds);
            while (light != null && Time.time < _endTime)
            {
                yield return wait;
                if (light == null || caster.PrefabProps == null)
                    break;

                var position = GetLightPosition(caster);
                if ((position - light.transform.position).sqrMagnitude >= _minMove * _minMove)
                    _stationaryLightsService.MoveLight(light, position);
            }

            if (light != null)
                Object.Destroy(light);
            if (_light == light)
                _light = null;
        }

        /// <summary>
        /// Above the caster's head: the VR hero's head is the camera.
        /// </summary>
        private static Vector3 GetLightPosition(NpcContainer caster)
        {
            var camera = Camera.main;
            var head = caster.IsHero() && camera != null
                ? camera.transform.position
                : caster.Go.transform.position + Vector3.up;
            return head + Vector3.up * _heightAboveHead;
        }
    }
}
