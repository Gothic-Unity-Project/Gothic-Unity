using Gothic.Core.Models.Container;
using Gothic.Core.Services.Npc;
using MyBox;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;

namespace Gothic.Core.Services.Meshes
{
    public class ParticleService
    {
        [Inject] private readonly MeshService _meshService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly FightService _fightService;

        public void Init()
        {
            GlobalEventDispatcher.FightHit.AddListener(EmitBlood);
        }

        private void EmitBlood(NpcContainer attacker, NpcContainer target, Vector3 position, ItemInstance _)
        {
            if (target == null || target.Instance == null)
                return;

            // A parried or dodged hit doesn't land.
            if (attacker != null && _fightService.IsBlocked(attacker, target))
                return;

            // Resolve guild-specific blood data
            var guild = target.Instance.Guild;

            // Emitter string used by MeshBuilder/PFX setup
            var emitter = _gameStateService?.GuildValues?.GetBloodEmitter(guild);
            if (emitter.IsNullOrEmpty())
                return;

            // Create particle effect at the hit position
            _meshService.CreateVobPfx(emitter, position, Quaternion.identity, parent: target.Go, destroyAfterPlay: true);
        }
    }
}
