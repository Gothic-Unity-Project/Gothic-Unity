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
        // DeveloperConfig.EnableSpellEffectGothicMotion: at least this many particles per second (Gothic's INIT effects
        // emit 50-500/s; a few emitted ~5/s in our builder once).
        private const float _minEmissionRate = 50f;
        private const float _minFitScale = 0.25f;

        /// <param name="fitRadius">The ring's slot: the whole effect is scaled down to this radius (parent's units) -
        /// Gothic's effects are hand-sized (a fireball's flames grow to 64 cm) and the ring is 2.2 times bigger.</param>
        public static GameObject Create(ItemInstance item, GameObject parent, DaedalusVm vm, VmCacheService vmCache,
            MeshService meshService, bool isGothicMotion, float fitRadius = 0f)
        {
            var namesSymbol = vm.GetSymbolByName("spellFXInstanceNames");
            var mfxName = item.Spell >= 0 ? namesSymbol?.GetString((ushort)item.Spell) : null;
            if (string.IsNullOrEmpty(mfxName))
                return null;

            var candidates = new[]
            {
                $"MFX_{mfxName.ToUpper()}_INIT",
                vmCache.TryGetVfxData($"spellFX_{mfxName}")?.VisNameS,
                vmCache.TryGetVfxEmitKey($"spellFX_{mfxName}_KEY_INIT")?.VisNameS,
                // Chain lightning: its own visual is a texture, the hand's effect is the one it creates (ORIGIN).
                GetCreatedVisual(vmCache, mfxName)
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
                PrepareHeldEffect(effect, isGothicMotion);
                if (isGothicMotion && fitRadius > 0f)
                    FitInto(effect, fitRadius);
                return effect;
            }
            return null;
        }

        /// <summary>
        /// The visual of the effect the spell's VISUALFX creates with it (emFXCreate_S) - chain lightning:
        /// spellFX_ChainLightning_ORIGIN = MFX_Lightning_Origin in the hand.
        /// </summary>
        public static string GetCreatedVisual(VmCacheService vmCache, string mfxName)
        {
            var created = vmCache.TryGetVfxData($"spellFX_{mfxName}")?.EmFxCreateS;
            return string.IsNullOrEmpty(created) ? null : vmCache.TryGetVfxData(created)?.VisNameS;
        }

        /// <summary>
        /// An INIT effect held in a hand or shown in the ring: every system loops and scales with its parent. With
        /// DeveloperConfig.EnableSpellEffectGothicMotion it keeps Gothic's own motion and emission (the transformation's
        /// lightning flies out at 1.5 m/s, summoning glows with 500 particles/s); without it every particle stood still
        /// (0-0.05 m/s) at 50/s - lightning trails that don't move don't show, summoning was a tenth.
        /// </summary>
        public static void PrepareHeldEffect(GameObject effect, bool isGothicMotion)
        {
            foreach (var ps in effect.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.loop = true;
                // Default (Local) ignores the parent's scale - the invest level size and the ring's fit wouldn't show.
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                var emission = ps.emission;
                if (isGothicMotion)
                {
                    // Moving with the hand/ring, not left behind in the world.
                    main.simulationSpace = ParticleSystemSimulationSpace.Local;
                    if (emission.rateOverTime.constantMax < _minEmissionRate)
                        emission.rateOverTime = _minEmissionRate;
                }
                else
                {
                    main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.05f);
                    var velocity = ps.velocityOverLifetime;
                    velocity.enabled = false;
                    emission.rateOverTime = _minEmissionRate;
                }
                ps.Play();
            }
        }

        /// <summary>
        /// Scales the effect down until its farthest particle (emitter shape + flight + half its biggest size) is within
        /// the radius. Never up.
        /// </summary>
        private static void FitInto(GameObject effect, float radius)
        {
            var extent = 0f;
            foreach (var ps in effect.GetComponentsInChildren<ParticleSystem>(true))
                extent = Mathf.Max(extent, GetExtent(ps));
            if (extent <= radius)
                return;
            // Not to a dot: a burst of sparks (the transformation's lightning, 1 m) stays visible over the slot.
            effect.transform.localScale *= Mathf.Max(_minFitScale, radius / extent);
        }

        private static float GetExtent(ParticleSystem ps)
        {
            var main = ps.main;
            var shape = ps.shape;
            var shapeRadius = 0f;
            if (shape.enabled)
            {
                var shapeScale = Mathf.Max(shape.scale.x, Mathf.Max(shape.scale.y, shape.scale.z));
                shapeRadius = shape.shapeType == ParticleSystemShapeType.Box
                    ? shapeScale * 0.5f
                    : shape.radius * shapeScale;
            }

            var sizeScale = 1f;
            var sizeOverLifetime = ps.sizeOverLifetime;
            if (sizeOverLifetime.enabled)
            {
                var curve = sizeOverLifetime.size;
                sizeScale = Mathf.Max(curve.Evaluate(0f, 1f), curve.Evaluate(1f, 1f));
            }

            // The average flight, not the fastest particle's.
            var speed = (main.startSpeed.constantMin + main.startSpeed.constantMax) * 0.5f;
            return shapeRadius + speed * main.startLifetime.constantMax +
                   main.startSize.constantMax * sizeScale * 0.5f;
        }
    }
}
#endif
