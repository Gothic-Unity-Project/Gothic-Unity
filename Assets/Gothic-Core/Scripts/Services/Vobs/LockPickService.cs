using System;
using System.Collections.Generic;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Npc;
using Reflex.Attributes;
using ZenKit;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;
using Random = UnityEngine.Random;

namespace Gothic.Core.Services.Vobs
{
    /// <summary>
    /// The engine's rules for picking a lock (OpenGothic PlayerControl::processPickLock, Interactive::checkUseConditions):
    /// in G1 everybody can pick locks, in G2 only with the lock picking talent - else the scripts refuse
    /// (PLAYER_MOB_MISSING_LOCKPICK). A wrong turn breaks the lock pick by chance: in G2 by the hero's dexterity
    /// (dexterity in 100 holds), in G1 by the talent's value, the chance to break. G1's scripts set it when a person is
    /// created (Npc_Default: 90) and lower it by 40 per level (B_GiveSkill: 50 trained, 10 as a master) - the status
    /// menu and Npc_GetTalentValue read the same value. Every turn calls the scripts' G_PickLock(success,
    /// brokenOpen): it plays the sound, prints the message and in G2 a broken pick may be heard.
    /// </summary>
    public class LockPickService
    {
        private const string _pickLockFunction = "G_PickLock";
        private const string _missingLockPickFunction = "PLAYER_MOB_MISSING_LOCKPICK";

        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly ContextGameVersionService _contextGameVersionService;
        [Inject] private readonly NpcService _npcService;

        private readonly HashSet<string> _missingFunctionsReported = new();

        public bool CanPickLocks(NpcContainer hero)
        {
            if (_contextGameVersionService.IsGothic1())
                return true;

            return hero?.Vob?.GetTalent((int)VmGothicEnums.Talent.Picklock)?.Skill > 0;
        }

        /// <summary>
        /// A wrong turn: does the lock pick break?
        /// </summary>
        public bool RollsBreak(NpcContainer hero)
        {
            if (hero?.Vob == null)
                return false;

            if (_contextGameVersionService.IsGothic2())
                return hero.Vob.GetAttribute((int)NpcAttribute.Dexterity) <= Random.Range(0, 100);

            // The engine's roll (OpenGothic PlayerControl::processPickLock): 100 - value <= rand(100).
            var breakChance = hero.Vob.GetTalent((int)VmGothicEnums.Talent.Picklock)?.Value ?? 0;
            return Random.Range(0, 100) < breakChance;
        }

        /// <summary>
        /// The scripts react to a turn: G_PickLock(success, brokenOpen) - a correct turn (1, 0), the lock open (1, 1),
        /// a wrong turn (0, 0), a broken lock pick (0, 1). False if the scripts have none (mods) or it failed - the
        /// caller plays the sounds then.
        /// </summary>
        public bool OnTurn(bool isSuccess, bool isBrokenOpen)
        {
            return CallAsHero(_pickLockFunction, index =>
                _gameStateService.GothicVm.Call(index, isSuccess ? 1 : 0, isBrokenOpen ? 1 : 0));
        }

        /// <summary>
        /// The hero may not pick locks: the scripts' refusal (G2: "I can't pick locks.").
        /// </summary>
        public void Refuse()
        {
            CallAsHero(_missingLockPickFunction, index => _gameStateService.GothicVm.Call(index));
        }

        private bool CallAsHero(string functionName, Action<int> call)
        {
            var vm = _gameStateService.GothicVm;
            var function = vm?.GetSymbolByName(functionName);
            // Only script functions - calling an external crashes the VM.
            if (function is not { Type: DaedalusDataType.Function, IsExternal: false })
            {
                if (_missingFunctionsReported.Add(functionName))
                    Logger.LogWarning($"[LockPick] The scripts have no function {functionName} - not called",
                        LogCat.Vob);
                return false;
            }

            var hero = _npcService.GetHeroContainer();
            var oldSelf = vm.GlobalSelf;
            vm.GlobalSelf = hero?.Instance;
            try
            {
                call(function.Index);
                return true;
            }
            catch (Exception e)
            {
                Logger.LogError($"[LockPick] {functionName} failed: {e.Message}", LogCat.Vob);
                return false;
            }
            finally
            {
                vm.GlobalSelf = oldSelf;
            }
        }
    }
}
