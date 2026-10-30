using System;
using System.Collections.Generic;
using System.IO;
using Gothic.Core.Logging;
using Gothic.Core.Models.SaveGames;
using Gothic.Core.Services;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.World;
using JetBrains.Annotations;
using Newtonsoft.Json;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.SaveGames
{
    /// <summary>
    /// Reads/writes UNITYSAVE.json: a header to identify the build/game/mod which wrote the save, and the registered
    /// ISaveStateSection data.
    /// </summary>
    public class UnitySaveDomain
    {
        public const string FileName = "UNITYSAVE.json";

        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly ContextGameVersionService _contextGameVersionService;


        /// <param name="previousSave">Data of the save we loaded. Its unknown sections are kept.</param>
        public void Write(string folder, [CanBeNull] UnitySaveData previousSave,
            IReadOnlyDictionary<string, ISaveStateSection> sections)
        {
            var data = CreateData(CreateHeader(), previousSave, sections);
            File.WriteAllText(Path.Combine(folder, FileName), JsonConvert.SerializeObject(data, Formatting.Indented));
        }

        /// <summary>
        /// Sections of the previous save without a registered owner are kept. So is the previous data of a section
        /// which fails to capture.
        /// </summary>
        public static UnitySaveData CreateData(UnitySaveHeader header, [CanBeNull] UnitySaveData previousSave,
            IReadOnlyDictionary<string, ISaveStateSection> sections)
        {
            var data = new UnitySaveData { Header = header };

            if (previousSave != null)
            {
                foreach (var (key, section) in previousSave.Sections)
                    data.Sections[key] = section;
            }

            foreach (var (key, section) in sections)
            {
                try
                {
                    data.Sections[key] = new UnitySaveSection { Version = section.Version, Data = section.Capture() };
                }
                catch (Exception e)
                {
                    Logger.LogError($"[SaveGame] Section {key} couldn't be saved: {e}", LogCat.Loading);
                }
            }

            return data;
        }

        [CanBeNull]
        public UnitySaveData Read(string folder)
        {
            var path = Path.Combine(folder, FileName);
            if (!File.Exists(path))
                return null;

            try
            {
                return JsonConvert.DeserializeObject<UnitySaveData>(File.ReadAllText(path));
            }
            catch (Exception e)
            {
                Logger.LogError($"[SaveGame] {path} couldn't be read: {e.Message}", LogCat.Loading);
                return null;
            }
        }

        public void Restore([CanBeNull] UnitySaveData save, IReadOnlyDictionary<string, ISaveStateSection> sections)
        {
            if (save == null)
                return;

            WarnOnHeaderMismatch(save.Header);

            foreach (var (key, section) in sections)
            {
                if (!save.Sections.TryGetValue(key, out var data))
                    continue;

                try
                {
                    section.Restore(data.Data, data.Version);
                }
                catch (Exception e)
                {
                    Logger.LogError($"[SaveGame] Section {key} couldn't be restored: {e}", LogCat.Loading);
                }
            }
        }

        private UnitySaveHeader CreateHeader()
        {
            return new UnitySaveHeader
            {
                GothicUnityVersion = Application.version,
                Game = _contextGameVersionService.Version.ToString(),
                ModIni = _configService.EffectiveModIniFileName,
                ScriptSignature = CreateScriptSignature(),
                SavedAtUtc = DateTime.UtcNow.ToString("O")
            };
        }

        private void WarnOnHeaderMismatch(UnitySaveHeader header)
        {
            if (header == null)
                return;

            if (header.SchemaVersion > UnitySaveHeader.CurrentSchemaVersion)
                Logger.LogWarning($"[SaveGame] Save was written by a newer build ({header.GothicUnityVersion}).",
                    LogCat.Loading);

            if (header.ScriptSignature != CreateScriptSignature())
                Logger.LogWarning($"[SaveGame] Save was written with other scripts ({header.Game}, {header.ModIni}). " +
                                  "Removed or renamed script variables are skipped.", LogCat.Loading);
        }

        /// <summary>
        /// FNV-1a over all symbol names. Stable across sessions (string.GetHashCode() isn't).
        /// </summary>
        private string CreateScriptSignature()
        {
            var vm = _gameStateService.GothicVm;
            if (vm == null)
                return string.Empty;

            var hash = 2166136261u;
            for (var i = 0; i < vm.SymbolCount; i++)
            {
                var name = vm.GetSymbolByIndex(i)?.Name ?? string.Empty;
                foreach (var c in name)
                {
                    hash ^= c;
                    hash *= 16777619u;
                }
            }

            return $"{vm.SymbolCount}-{hash:x8}";
        }
    }
}
