using System.Linq;
using Gothic.Core.Extensions;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using MyBox;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;

namespace Gothic.Core.Services.Meshes
{
    public class ParticleService
    {
        // C_Item.material (MAT_*) as the engine's two letter tags.
        private static readonly string[] _materialTags = { "WO", "ST", "ME", "LE", "CL", "GL" };
        private const int _materialWood = 0;
        private const int _materialMetal = 2;

        [Inject] private readonly MeshService _meshService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly AudioService _audioService;

        public void Init()
        {
            GlobalEventDispatcher.FightHit.AddListener(EmitBlood);
            GlobalEventDispatcher.FightHit.AddListener(EmitHitEffect);
        }

        private void EmitBlood(NpcContainer _, NpcContainer target, Vector3 position, ItemInstance __)
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

        /// <summary>
        /// The sound and sparks of a hit by what hits on what it hits, like the engine (OpenGothic
        /// World::addWeaponHitEffect): CS_IAM_[weapon]_[armor] for a weapon, CS_MAM_[JA|FI]_[armor] for a monster's jaws or
        /// a fist - armor is metal (ME), wood (WO) or flesh (FL). The particles are CPFX_ of the same name, else the
        /// armor material's (CPFX_IAM_METAL).
        /// </summary>
        private void EmitHitEffect(NpcContainer attacker, NpcContainer target, Vector3 position, ItemInstance weapon)
        {
            if (target?.Instance == null || target.Go == null)
                return;

            if (position == default)
                position = target.Go.transform.position + Vector3.up;

            string scheme, source;
            if (weapon != null)
            {
                scheme = "IAM";
                source = GetMaterialTag(weapon.Material);
            }
            else
            {
                scheme = "MAM";
                source = attacker != null && !attacker.IsHuman() ? "JA" : "FI";
            }

            var armorTag = GetArmorTag(target);
            var clip = _audioService.GetRandomSoundClip($"CS_{scheme}_{source}_{armorTag}");
            if (clip != null)
                AudioSource.PlayClipAtPoint(clip, position);

            var pfxName = GetHitParticles(scheme, source, armorTag);
            if (pfxName != null)
                _meshService.CreateVobPfx(pfxName, position, Quaternion.identity, destroyAfterPlay: true);
        }

        private string GetHitParticles(string scheme, string source, string armorTag)
        {
            var name = $"CPFX_{scheme}_{source}_{armorTag}";
            if (IsParticleEffect(name))
                return name;

            name = armorTag switch
            {
                "ME" => $"CPFX_{scheme}_METAL",
                "WO" => $"CPFX_{scheme}_WOOD",
                "ST" => $"CPFX_{scheme}_STONE",
                _ => null
            };
            return name != null && IsParticleEffect(name) ? name : null;
        }

        private bool IsParticleEffect(string name)
        {
            var symbol = _gameStateService.PfxVm?.GetSymbolByName(name);
            return symbol is { Type: ZenKit.DaedalusDataType.Instance };
        }

        /// <summary>
        /// Like in the vanilla sounds, only metal and wooden armor sound different - everything else is flesh.
        /// </summary>
        private static string GetArmorTag(NpcContainer target)
        {
            var armor = target.Props?.EquippedItems
                .FirstOrDefault(i => i.MainFlag == (int)VmGothicEnums.ItemFlags.ItemKatArmor);
            return armor?.Material switch
            {
                _materialMetal => "ME",
                _materialWood => "WO",
                _ => "FL"
            };
        }

        private static string GetMaterialTag(int material)
        {
            return material >= 0 && material < _materialTags.Length ? _materialTags[material] : "UD";
        }
    }
}
