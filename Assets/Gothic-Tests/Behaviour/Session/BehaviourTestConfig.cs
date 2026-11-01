using System;
using System.Linq;
using Gothic.Core.Models.Config;
using Gothic.Core.Models.Context;
using Gothic.Core.Services.World;
using UnityEngine;
using ZenKit;
using Object = UnityEngine.Object;

namespace Gothic.Tests.Behaviour.Session
{
    /// <summary>
    /// The developer config the scenarios run with: a copy of Production made in code, changed where a scenario needs a
    /// fixed start (straight into the world, VR with the desktop simulator, everything spawned). No asset of its own -
    /// it would ship in builds and drift from Production. The game version is Production's: one version per run.
    /// </summary>
    public static class BehaviourTestConfig
    {
        private const string _productionPath = "DeveloperConfigs/Production";

        public static GameVersion GameVersion => LoadProduction().GameVersion;

        public static DeveloperConfig Create(string world)
        {
            var config = Object.Instantiate(LoadProduction());
            config.name = "BehaviourTest (copy of Production)";
            config.hideFlags = HideFlags.DontSave;

            config.PreselectGameVersion = true;
            config.PreSelectMod = false;
            config.GameControls = Controls.VR;
            config.EnableVRDeviceSimulator = true;
            config.PreselectMarvinMode = false;

            config.EnableMainMenu = false;
            config.LoadFromSaveSlot = false;
            config.PreselectWorldToSpawn = ToWorldToSpawn(world);
            config.SpawnAtWaypoint = string.Empty;
            config.SpeedUpLoading = true;
            config.SkipVideos = true;

            config.EnableVOBs = true;
            config.SpawnVOBTypes = new DeveloperConfig.VOBTypesCollection();
            config.EnableNpcs = true;
            config.SpawnNpcInstances = new DeveloperConfig.IntCollection();
            config.SpawnMonsterInstances = new DeveloperConfig.MonsterTypesCollection();
            config.EnableParticleEffects = true;
            config.SunUpdateInterval = GameTimeService.GameTimeInterval.EveryGameMinute;
            return config;
        }

        /// <summary>
        /// "world.zen" -> G1World. Only the worlds of DeveloperConfigEnums.WorldMappings can be started directly.
        /// </summary>
        public static DeveloperConfigEnums.WorldToSpawn ToWorldToSpawn(string world)
        {
            var mapping = DeveloperConfigEnums.WorldMappings
                .Where(entry => entry.Key != DeveloperConfigEnums.WorldToSpawn.None)
                .FirstOrDefault(entry => entry.Value.Equals(world, StringComparison.OrdinalIgnoreCase));
            if (mapping.Key == DeveloperConfigEnums.WorldToSpawn.None)
                throw new ArgumentException($"The world \"{world}\" can't be started directly. Known: " +
                    string.Join(", ", DeveloperConfigEnums.WorldMappings
                        .Where(entry => entry.Key != DeveloperConfigEnums.WorldToSpawn.None)
                        .Select(entry => entry.Value)));

            return mapping.Key;
        }

        private static DeveloperConfig LoadProduction()
        {
            var production = Resources.Load<DeveloperConfig>(_productionPath);
            if (production == null)
                throw new InvalidOperationException($"No developer config at Resources/{_productionPath}");

            return production;
        }
    }
}
