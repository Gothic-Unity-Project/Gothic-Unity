using System.Collections.Generic;
using Gothic.Core.Adapters.Npc;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Vobs;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Vobs;

namespace Gothic.Core.Adapters.Vob
{
    /// <summary>
    /// oCTouchDamage: hurts the hero and NPCs inside its zone right away and again after each repeat delay
    /// (TouchDamageService).
    /// </summary>
    public class TouchDamageHandler : MonoBehaviour
    {
        // A zone without a repeat delay hurts every frame in the engine - at most this often here.
        private const float _minRepeatSeconds = 0.1f;

        [Inject] private readonly TouchDamageService _touchDamageService;
        [Inject] private readonly NpcService _npcService;

        private ITouchDamage _zone;
        private readonly HashSet<NpcContainer> _inside = new();
        private float _nextDamageTime;

        private void Awake() => this.Inject();

        public void Init(ITouchDamage zone) => _zone = zone;

        private void OnTriggerEnter(Collider other)
        {
            var npc = GetNpc(other);
            if (npc != null)
                _inside.Add(npc);
        }

        private void OnTriggerExit(Collider other)
        {
            var npc = GetNpc(other);
            if (npc != null)
                _inside.Remove(npc);
        }

        private void Update()
        {
            if (_zone == null || _inside.Count == 0 || Time.time < _nextDamageTime)
                return;

            _inside.RemoveWhere(npc => npc?.Go == null);
            foreach (var npc in _inside)
                _touchDamageService.Damage(npc, _zone);

            _nextDamageTime = Time.time + Mathf.Max((float)_zone.RepeatDelay.TotalSeconds, _minRepeatSeconds);
        }

        private NpcContainer GetNpc(Collider other)
        {
            if (other.CompareTag(Constants.PlayerTag))
                return _npcService.GetHeroContainer();

            // The NPC's body, not its weapon's hit box.
            return other.isTrigger ? null : other.GetComponentInParent<NpcLoader>()?.Container;
        }
    }
}
