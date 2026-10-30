using Gothic.Core.Models.Container;
using MyBox;
using Reflex.Attributes;
using UnityEngine;

namespace Gothic.Core.Services.Meshes
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

            // Create particle effect at the hit position (placed relative to the target, which it follows).
            var localPosition = target.Go != null ? target.Go.transform.InverseTransformPoint(position) : position;
            _meshService.CreateVobPfx(emitter, localPosition, Quaternion.identity, parent: target.Go,
                destroyAfterPlay: true);
        }
    }
}
