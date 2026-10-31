using System.IO;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Npc;
using Gothic.Core.Models.Proxy;
using Gothic.Core.Services.Caches;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Npc
{
    /// <summary>
    /// Transformation spells (Trf_Wolf, Trf_Scavenger, ...). Their Spell_Logic_Trf_X calls Npc_SetActiveSpellInfo(self,
    /// monster) and casts; then, like the engine (OpenGothic Npc::TransformBack), the scripts see the hero as that
    /// monster: its guild decides who is friendly (wolves) and who attacks (hunters, guards). The VR player moves at the
    /// monster's walk and run speed and looks around from its eye height (HeroTransformed). Casting again (the trigger)
    /// turns him back, like Gothic's action key. Found on BoroBongo's branch.
    /// </summary>
    public class TransformationService
    {
        private const float _eyeHeightShare = 0.9f;
        private const float _minEyeHeight = 0.4f;

        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly AnimationService _animationService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;

        private NpcContainer _hero;
        private int _originalGuild;
        private int _originalVobGuild;
        private int _originalVobGuildTrue;

        public HeroTransformation Current { get; private set; }
        public bool IsTransformed => Current != null;

        public TransformationService()
        {
            // A new world or a loaded game starts human.
            GlobalEventDispatcher.LoadingSceneLoaded.AddListener(TransformBack);
            GlobalEventDispatcher.LoadGameStart.AddListener(TransformBack);
        }

        /// <returns>False if the monster can't be read (an instance without a visual) - the hero stays human.</returns>
        public bool Transform(NpcContainer hero, int monsterIndex)
        {
            if (hero?.Instance == null || IsTransformed)
                return false;

            var monster = ReadMonster(hero, monsterIndex);
            if (monster == null)
                return false;

            _hero = hero;
            _originalGuild = hero.Instance.Guild;
            _originalVobGuild = hero.Vob.Guild;
            _originalVobGuildTrue = hero.Vob.GuildTrue;
            hero.Instance.Guild = monster.Guild;
            hero.Vob.Guild = monster.Guild;
            hero.Vob.GuildTrue = monster.Guild;

            Current = monster;
            Logger.Log($"[Transformation] Hero turns into {monster.MonsterName} (guild {_originalGuild} -> " +
                       $"{monster.Guild}, run x{monster.RunSpeedFactor:F2}, eyes at {monster.EyeHeight:F2} m).",
                LogCat.Fight);
            GlobalEventDispatcher.HeroTransformed.Invoke(monster);
            return true;
        }

        public void TransformBack()
        {
            if (!IsTransformed)
                return;

            if (_hero?.Instance != null)
            {
                _hero.Instance.Guild = _originalGuild;
                _hero.Vob.Guild = _originalVobGuild;
                _hero.Vob.GuildTrue = _originalVobGuildTrue;
            }

            Logger.Log($"[Transformation] Hero is human again (was {Current.MonsterName}).", LogCat.Fight);
            Current = null;
            _hero = null;
            GlobalEventDispatcher.HeroTransformedBack.Invoke();
        }

        /// <summary>
        /// Runs the monster's constructor on a scratch instance (its guild and model are set there), like the engine
        /// initializes the hero with it.
        /// </summary>
        private HeroTransformation ReadMonster(NpcContainer hero, int monsterIndex)
        {
            var vm = _gameStateService.GothicVm;
            var symbol = monsterIndex > 0 ? vm.GetSymbolByIndex(monsterIndex) : null;
            // Only instances: initializing anything else aborts natively.
            if (symbol == null || symbol.Type != DaedalusDataType.Instance)
                return null;

            var instance = vm.AllocInstance<NpcInstance>(symbol);
            var scratch = new NpcContainer { Instance = instance, Vob = new NpcProxy(monsterIndex), Props = new() };
            instance.UserData = scratch;
            // The scripts' externals look NPCs up there while the constructor runs.
            _multiTypeCacheService.NpcCache.Add(scratch);
            try
            {
                vm.InitInstance(instance);
            }
            finally
            {
                _multiTypeCacheService.NpcCache.Remove(scratch);
            }

            var mds = scratch.Props.MdsNameBase;
            if (string.IsNullOrEmpty(mds))
            {
                Logger.LogWarning($"[Transformation] {symbol.Name} has no model - no transformation.", LogCat.Fight);
                return null;
            }

            var hierarchy = _resourceCacheService.TryGetModelHierarchy(Path.GetFileNameWithoutExtension(mds), false);
            var height = hierarchy != null ? (hierarchy.BoundingBox.Max.Y - hierarchy.BoundingBox.Min.Y) / 100f : 1f;
            return new HeroTransformation
            {
                MonsterName = instance.GetName(NpcNameSlot.Slot0),
                Guild = instance.Guild,
                WalkSpeedFactor = GetFactor(GetSpeed(scratch, "S_FISTWALKL", "S_WALKL"), GetSpeed(hero, "S_WALKL")),
                RunSpeedFactor = GetFactor(GetSpeed(scratch, "S_FISTRUNL", "S_RUNL"), GetSpeed(hero, "S_RUNL")),
                EyeHeight = Mathf.Max(_minEyeHeight, height * _eyeHeightShare)
            };
        }

        private static float GetFactor(float monsterSpeed, float humanSpeed)
        {
            return monsterSpeed > 0f && humanSpeed > 0f ? monsterSpeed / humanSpeed : 1f;
        }

        /// <summary>
        /// The root motion of the NPC's walk or run loop (m/s).
        /// </summary>
        private float GetSpeed(NpcContainer npc, params string[] animationNames)
        {
            foreach (var name in animationNames)
            {
                var track = _animationService.GetTrack(name, npc.Props.MdsNameBase, npc.Props.MdsNameOverlay);
                if (track != null && track.MovementSpeed.z > 0f)
                    return track.MovementSpeed.z;
            }
            return 0f;
        }
    }
}
