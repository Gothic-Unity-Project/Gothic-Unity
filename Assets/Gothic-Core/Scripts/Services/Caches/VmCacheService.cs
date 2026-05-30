using System;
using System.Collections.Generic;
using System.IO;
using GUZ.Core.Models.Proxy;
using GUZ.Core.Const;
using GUZ.Core.Extensions;
using GUZ.Core.Models.Audio;
using JetBrains.Annotations;
using Reflex.Attributes;
using ZenKit.Daedalus;

namespace GUZ.Core.Services.Caches
{
    public class VmCacheService
    {
        [Inject] private readonly GameStateService _gameStateService;
        
        private readonly Dictionary<string, ItemInstance> _itemDataCache = new();
        private readonly Dictionary<string, FightAiProxy> _fightAiDataCache = new();
        private readonly Dictionary<int, SvmInstance> _svmDataCache = new();
        private readonly Dictionary<string, SfxModel> _sfxDataCache = new();
        private readonly Dictionary<string, ParticleEffectInstance> _pfxDataCache = new();

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

            return TryGetItemData(symbol.Name);
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

        public void Dispose()
        {
            _itemDataCache.Clear();
            _svmDataCache.Clear();
            _sfxDataCache.Clear();
            _pfxDataCache.Clear();
        }
    }
}
