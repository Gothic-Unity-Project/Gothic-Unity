using System;
using System.Collections.Generic;
using System.IO;
using Gothic.Core.Models.Audio;
using Gothic.Core.Models.Proxy;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Services.Config;
using Gothic.Core.Models.Vm;
using JetBrains.Annotations;
using Reflex.Attributes;
using ZenKit.Daedalus;
using ZenKit.Vobs;

namespace Gothic.Core.Services.Caches
{
    public class VmCacheService
    {
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly ConfigService _configService;
        
        private readonly Dictionary<string, ItemInstance> _itemDataCache = new();
        private readonly Dictionary<string, FightAiProxy> _fightAiDataCache = new();
        private readonly Dictionary<int, SvmInstance> _svmDataCache = new();
        private readonly Dictionary<string, SfxModel> _sfxDataCache = new();
        private readonly Dictionary<string, ParticleEffectInstance> _pfxDataCache = new();
        private readonly Dictionary<string, EffectBaseInstance> _vfxDataCache = new();
        private readonly Dictionary<string, ParticleEffectEmitKeyInstance> _vfxEmitKeyCache = new();
        private readonly Dictionary<int, SpellInstance> _spellDataCache = new();

        // DeveloperConfig.EnableDigSpotTreasureOnly: the dig functions - everything else used with a melee tool opens a
        // mob dialog (Mroczne Tajemnice's anvil SMITHERY with the sledgehammer, G2's Goldhacken/Stomp) and is used like
        // other mobs.
        private static readonly string[] _digStateFunctions = { "B_SCGETTREASURE" };

        /// <summary>
        /// Dig spot: an interactive mob (no door/chest) with an onStateFunc which is used with a melee weapon/tool,
        /// e.g. G2's treasure X marks + pickaxe. Generic for G2 and mods.
        /// </summary>
        public bool IsDigSpot([CanBeNull] IVirtualObject vob)
        {
            if (vob is not IInteractiveObject mob || vob is IDoor || vob is IContainer)
                return false;
            if (string.IsNullOrEmpty(mob.OnStateChangeFunction) || string.IsNullOrEmpty(mob.Item))
                return false;
            if (_configService.Dev.EnableDigSpotTreasureOnly &&
                Array.FindIndex(_digStateFunctions, f => f.EqualsIgnoreCase(mob.OnStateChangeFunction)) < 0)
                return false;

            var tool = TryGetItemData(mob.Item);
            return tool != null && ((VmGothicEnums.ItemFlags)tool.MainFlag & VmGothicEnums.ItemFlags.ItemKatNf) != 0;
        }

        /// <summary>
        /// Hint: Instances only need to be initialized once in ZenKit.
        /// There are two ways of getting Item data. Via INSTANCE name or symbolIndex inside VM.
        /// </summary>
        public ItemInstance TryGetItemData(int instanceId)
        {
            var symbol = _gameStateService.GothicVm.GetSymbolByIndex(instanceId);

            if (symbol == null)
            {
                return null;
            }

            // DeveloperConfig.EnableItemDataBySymbol: the name lookup lowercases the key, ZenKit only matches ASCII
            // case-insensitively - Renovation's ITAR_ŃASTOFFS (Alrik's rags) became itar_ńastoffs, "Symbol not found",
            // and Mdl_SetVisualBody stopped the whole world's NPC creation.
            if (!_configService.Dev.EnableItemDataBySymbol)
                return TryGetItemData(symbol.Name);

            var preparedKey = GetPreparedKey(symbol.Name);
            if (_itemDataCache.TryGetValue(preparedKey, out var data) && data != null)
                return data;

            ItemInstance newData = null;
            try
            {
                newData = _gameStateService.GothicVm.InitInstance<ItemInstance>(symbol);
            }
            catch (Exception)
            {
                // ignored - like the name lookup: not an item
            }

            _itemDataCache[preparedKey] = newData;
            return newData;
        }

        /// <summary>
        /// Hint: Instances only need to be initialized once in ZenKit.
        /// There are two ways of getting Item data. Via INSTANCE name or symbolIndex inside VM.
        /// </summary>
        [CanBeNull]
        public ItemInstance TryGetItemData(string key)
        {
            var preparedKey = GetPreparedKey(key);
            if (_itemDataCache.TryGetValue(preparedKey, out var data))
            {
                return data;
            }

            ItemInstance newData = null;
            try
            {
                newData = _gameStateService.GothicVm.InitInstance<ItemInstance>(preparedKey);
            }
            catch (Exception)
            {
                // ignored
            }

            _itemDataCache[preparedKey] = newData;

            return newData;
        }

        public FightAiProxy TryGetFightAiData(string nameTemplate, int instanceId)
        {
            var preparedKey = GetPreparedKey(string.Format(nameTemplate, instanceId));
            
            if (_fightAiDataCache.TryGetValue(preparedKey, out var data))
                return data;

            FightAiProxy newData = null;
            try
            {
                newData = new FightAiProxy(_gameStateService.FightVm.InitInstance<FightAiInstance>(preparedKey));
            }
            catch (Exception)
            {
                // ignored
            }

            _fightAiDataCache[preparedKey] = newData;

            return newData;
        }
        
        /// <summary>
        /// Hint: Instances only need to be initialized once in ZenKit.
        /// </summary>
        [CanBeNull]
        public SvmInstance TryGetSvmData(int voiceId)
        {
            if (_svmDataCache.TryGetValue(voiceId, out var data))
            {
                return data;
            }

            SvmInstance newData = null;
            try
            {
                newData = _gameStateService.GothicVm.InitInstance<SvmInstance>($"SVM_{voiceId}");
            }
            catch (Exception)
            {
                // ignored
            }

            _svmDataCache[voiceId] = newData;

            return newData;
        }

        /// <summary>
        /// Hint: Instances only need to be initialized once in ZenKit and don't need to be deleted during runtime.
        /// Hint: As there is a potential for multiple instances per key
        ///       (e.g., BreathBubbles, BreathBubbles_A1, BreathBubbles_A2), we need to retrieve a container holding all of them.
        /// </summary>
        [CanBeNull]
        public SfxModel TryGetSfxData(string key)
        {
            var preparedKey = GetPreparedKey(key);
            if (_sfxDataCache.TryGetValue(preparedKey, out var data))
            {
                return data;
            }

            SfxModel newData = null;
            newData = new SfxModel(preparedKey);

            _sfxDataCache[preparedKey] = newData;

            return newData;
        }

        /// <summary>
        /// C_Spell data of a spell ID. Like the engine, the instance name is "SPELL_" + spellFXInstanceNames[spellId]
        /// (e.g. 0 -> "Light" -> Spell_Light). Works for G1, G2 and mods as long as they follow this convention.
        /// </summary>
        [CanBeNull]
        public SpellInstance TryGetSpellData(int spellId)
        {
            if (_spellDataCache.TryGetValue(spellId, out var data))
                return data;

            SpellInstance newData = null;
            try
            {
                var spellName = _gameStateService.GothicVm.GetSymbolByName("spellFXInstanceNames")?.GetString((ushort)spellId);
                if (!string.IsNullOrEmpty(spellName))
                    newData = _gameStateService.GothicVm.InitInstance<SpellInstance>($"SPELL_{spellName}");
            }
            catch (Exception)
            {
                // ignored
            }

            _spellDataCache[spellId] = newData;

            return newData;
        }

        /// <summary>
        /// Hint: Instances only need to be initialized once in ZenKit and don't need to be deleted during runtime.
        /// </summary>
        public ParticleEffectInstance TryGetPfxData(string key)
        {
            var preparedKey = GetPreparedKey(key);
            if (_pfxDataCache.TryGetValue(preparedKey, out var data))
            {
                return data;
            }

            ParticleEffectInstance newData = null;
            try
            {
                newData = _gameStateService.PfxVm.InitInstance<ParticleEffectInstance>(preparedKey);
            }
            catch (Exception)
            {
                // ignored
            }

            _pfxDataCache[preparedKey] = newData;

            return newData;
        }

        /// <summary>
        /// Spell effect from VISUALFX.DAT (C_VisualFX, e.g. spellFX_Fireball). Null if missing.
        /// </summary>
        public EffectBaseInstance TryGetVfxData(string key)
        {
            var preparedKey = GetPreparedKey(key);
            if (_vfxDataCache.TryGetValue(preparedKey, out var data))
                return data;

            EffectBaseInstance newData = null;
            try
            {
                if (_gameStateService.VfxVm?.GetSymbolByName(preparedKey) != null)
                    newData = _gameStateService.VfxVm.InitInstance<EffectBaseInstance>(preparedKey);
            }
            catch (Exception)
            {
                // ignored
            }

            _vfxDataCache[preparedKey] = newData;
            return newData;
        }

        /// <summary>
        /// Key of a spell effect from VISUALFX.DAT (C_ParticleFXEmitKey, e.g. spellFX_Fireball_KEY_CAST). Null if missing.
        /// </summary>
        public ParticleEffectEmitKeyInstance TryGetVfxEmitKey(string key)
        {
            var preparedKey = GetPreparedKey(key);
            if (_vfxEmitKeyCache.TryGetValue(preparedKey, out var data))
                return data;

            ParticleEffectEmitKeyInstance newData = null;
            try
            {
                if (_gameStateService.VfxVm?.GetSymbolByName(preparedKey) != null)
                    newData = _gameStateService.VfxVm.InitInstance<ParticleEffectEmitKeyInstance>(preparedKey);
            }
            catch (Exception)
            {
                // ignored
            }

            _vfxEmitKeyCache[preparedKey] = newData;
            return newData;
        }

        private string GetPreparedKey(string key)
        {
            var lowerKey = key.ToLower();
            var extension = Path.GetExtension(lowerKey);

            if (extension == string.Empty)
            {
                return lowerKey;
            }

            return lowerKey.Replace(extension, "");
        }

        /// <summary>
        /// DeveloperConfig.EnableVmCacheResetOnLoad: loading a game creates a new GOTHIC VM - the item, SVM and spell
        /// instances of the old one are dead there. Wld_DetectItem set the global 'item' to an old item: scripts read
        /// "C_ITEM.MAINFLAG without an instance set", Npc_CanSeeItem(slf, item) failed its cast and left the VM stack
        /// broken until it crashed natively ("stack overflow" - Mroczne Tajemnice's giant skeleton in the abandoned mine).
        /// </summary>
        public void ResetGothicVmInstances()
        {
            _itemDataCache.Clear();
            _svmDataCache.Clear();
            _spellDataCache.Clear();
        }

        public void Dispose()
        {
            _itemDataCache.Clear();
            _svmDataCache.Clear();
            _sfxDataCache.Clear();
            _pfxDataCache.Clear();
            _spellDataCache.Clear();
        }
    }
}
