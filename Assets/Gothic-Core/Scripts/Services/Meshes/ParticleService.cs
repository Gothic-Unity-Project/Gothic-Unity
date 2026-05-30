using GUZ.Core.Models.Container;
using MyBox;
using Reflex.Attributes;
using UnityEngine;

namespace GUZ.Core.Services.Meshes
{
    public class ParticleService
    {
        [Inject] private readonly MeshService _meshService;
        [Inject] private readonly GameStateService _gameStateService;

        public void Init()
        {
            GlobalEventDispatcher.FightHit.AddListener(EmitBlood);
        }

        private void EmitBlood(NpcContainer _, NpcContainer target, Vector3 position)
        {
            if (target == null || target.Instance == null)
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
