#if GOTHIC_HVR_INSTALLED
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Meshes;
using UnityEngine;
using ZenKit;
using ZenKit.Daedalus;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// DeveloperConfig.EnableVrSpellRing: a spell's effect as it looks in a hand (like VRRuneCaster.SpawnSpellVfx):
    /// MFX_[NAME]_INIT, else the spell's VISUALFX visual. Looping, kept small. For the ring's preview and the spell
    /// taken into a hand.
    /// </summary>
    public static class VRSpellEffect
    {
        public static GameObject Create(ItemInstance item, GameObject parent, DaedalusVm vm, VmCacheService vmCache,
            MeshService meshService)
        {
            var namesSymbol = vm.GetSymbolByName("spellFXInstanceNames");
            var mfxName = item.Spell >= 0 ? namesSymbol?.GetString((ushort)item.Spell) : null;
            if (string.IsNullOrEmpty(mfxName))
                return null;

            var candidates = new[]
            {
                $"MFX_{mfxName.ToUpper()}_INIT",
                vmCache.TryGetVfxData($"spellFX_{mfxName}")?.VisNameS,
                vmCache.TryGetVfxEmitKey($"spellFX_{mfxName}_KEY_INIT")?.VisNameS
            };
            foreach (var pfxName in candidates)
            {
                if (string.IsNullOrEmpty(pfxName))
                    continue;
                var leaf = meshService.CreateVobPfx(pfxName, parent: parent);
                if (leaf == null)
                    continue;

                var effect = leaf.transform.parent != null && leaf.transform.parent.gameObject != parent
                    ? leaf.transform.parent.gameObject
                    : leaf;
                foreach (var ps in effect.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = ps.main;
                    main.loop = true;
                    main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.05f);
                    var velocity = ps.velocityOverLifetime;
                    velocity.enabled = false;
                    // Gothic's INIT effects emit ~5/s in our builder - hardly visible (VRRuneCaster boosts them too).
                    var emission = ps.emission;
                    emission.rateOverTime = 50f;
                    ps.Play();
                }
                return effect;
            }
            return null;
        }
    }
}
#endif
