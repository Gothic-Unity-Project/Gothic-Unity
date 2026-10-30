using System;
using System.Collections.Generic;
using System.IO;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Meshes;
using Reflex.Attributes;
using UnityEngine;
using ZenKit;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;
using Object = UnityEngine.Object;

namespace Gothic.Core.Domain.Npc
{
    /// <summary>
    /// The look of spells, from the scripts' visual effects (VisualFxInst.d, OpenGothic GameScript::spellVfx):
    /// spellFX_[name] is the glow at the caster or the flying projectile, its emFXCollDyn_S effect shows where the spell
    /// hits. Only the effects' particles are shown - their meshes, lights, invest keys and sounds aren't yet. Owned by
    /// MagicService.
    /// </summary>
    public class SpellEffectDomain
    {
        [Inject] private readonly ResourceCacheService _resourceCacheService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly MeshService _meshService;

        private readonly Dictionary<string, EffectBaseInstance> _effects = new(StringComparer.OrdinalIgnoreCase);
        private DaedalusVm _vfxVm;
        private bool _isVfxVmLoaded;

        /// <summary>
        /// The visual effect of that name (spellFX_Firebolt), null if the scripts have none.
        /// </summary>
        public EffectBaseInstance GetEffect(string name)
        {
            LoadVm();
            if (_vfxVm == null || string.IsNullOrEmpty(name))
                return null;
            if (_effects.TryGetValue(name, out var cached))
                return cached;

            EffectBaseInstance effect = null;
            if (_vfxVm.IsInstanceOf(_vfxVm.GetSymbolByName(name), "CFX_BASE"))
            {
                try
                {
                    effect = _vfxVm.InitInstance<EffectBaseInstance>(name);
                }
                catch (Exception e)
                {
                    Logger.LogWarning($"[Magic] Visual effect {name} failed: {e.Message}", LogCat.Fight);
                }
            }

            _effects[name] = effect;
            return effect;
        }

        /// <summary>
        /// The effect's particles at the position. Null if it shows none.
        /// </summary>
        /// <param name="lifetimeSeconds">They vanish after this long (looping ones too). 0 keeps them.</param>
        public GameObject CreateParticles(EffectBaseInstance effect, Vector3 position, Quaternion rotation,
            float lifetimeSeconds)
        {
            var pfxName = effect?.VisNameS;
            if (string.IsNullOrEmpty(pfxName))
                return null;

            pfxName = Path.GetFileNameWithoutExtension(pfxName);
            var pfxVm = _gameStateService.PfxVm;
            if (!pfxVm.IsInstanceOf(pfxVm?.GetSymbolByName(pfxName), "C_PARTICLEFX"))
                return null;

            var particles = _meshService.CreateVobPfx(pfxName, position, rotation,
                destroyAfterPlay: lifetimeSeconds > 0f);
            if (particles != null && lifetimeSeconds > 0f)
                Object.Destroy(particles, lifetimeSeconds);
            return particles;
        }

        private void LoadVm()
        {
            if (_isVfxVmLoaded)
                return;

            _isVfxVmLoaded = true;
            _vfxVm = _resourceCacheService.TryGetDaedalusVm("VISUALFX");
            if (_vfxVm == null)
                Logger.LogWarning("[Magic] VISUALFX scripts not found - spells have no particles.", LogCat.Fight);
        }
    }
}
