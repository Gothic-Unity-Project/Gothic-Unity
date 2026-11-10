using System;
using System.Collections.Generic;
using Gothic.Core.Domain.Vm;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Services;
using Gothic.Core.Services.Npc;
using Reflex.Attributes;
using ZenKit;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Npc
{
    /// <summary>
    /// G2's pickpocketing is a dialog (B_Beklauen.d): each NPC who can be robbed has DIA_[name]_PICKPOCKET
    /// (permanent, nr 900), its condition is C_Beklauen(TheftDex, TheftGold) with two literals, its choice runs
    /// B_Beklauen. This worker asks that dialog for the VR purse:
    /// - IsOffered: the info's condition with self = NPC, other = hero - true exactly when the dialog would offer it
    ///   (hero has the talent, not robbed yet, DEX >= TheftDex - Theftdiff, NpcObsessedByDMT == FALSE).
    /// - Steal: B_Beklauen with the globals the dialog would have set - C_Beklauen sets TheftDexGlob/TheftGoldGlob
    ///   only in ZS_Talk, never outside a dialog. Gold, XP, the aivar, the sound or the attack come from the script.
    /// TheftDex/TheftGold: Daedalus keeps a function's parameters in global symbols (C_BEKLAUEN.THEFTDEX). They are set
    /// to a marker before the condition runs - a changed value afterwards is the literal, and proves the condition
    /// called C_Beklauen (an info only named _PICKPOCKET, e.g. in a mod, gets no purse).
    /// Results are cached per NPC until the hero's DEX or talent, the NPC's AIV_PlayerHasPickedMyPocket or
    /// NpcObsessedByDMT change.
    /// </summary>
    public class PickpocketDialogDomain
    {
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly NpcHelperService _npcHelperService;
        [Inject] private readonly NpcService _npcService;

        private const string _infoSuffix = "_PICKPOCKET";
        private const string _theftDexParameterName = "C_BEKLAUEN.THEFTDEX";
        private const string _theftGoldParameterName = "C_BEKLAUEN.THEFTGOLD";
        private const string _theftDexGlobalName = "THEFTDEXGLOB";
        private const string _theftGoldGlobalName = "THEFTGOLDGLOB";
        private const string _theftDiffName = "THEFTDIFF";
        private const string _stealFunctionName = "B_BEKLAUEN";
        private const string _pickedAivarName = "AIV_PLAYERHASPICKEDMYPOCKET";
        private const string _obsessedName = "NPCOBSESSEDBYDMT";
        private const string _talentName = "NPC_TALENT_PICKPOCKET";
        // C_Beklauen: TheftDex <= 20 is child's play ("Kinderspiel klappt immer") - TheftDexGlob = 10.
        private const int _childsPlayTheftDex = 20;
        private const int _childsPlayTheftDexGlob = 10;
        private const int _marker = int.MinValue;

        private readonly Dictionary<NpcContainer, Entry> _entries = new();

        private DaedalusVm _vm;
        private bool _hasSymbols;
        private DaedalusSymbol _theftDexParameter;
        private DaedalusSymbol _theftGoldParameter;
        private DaedalusSymbol _theftDexGlobal;
        private DaedalusSymbol _theftGoldGlobal;
        private DaedalusSymbol _obsessed;
        private int _stealFunctionIndex;
        private int _pickedAivar;
        private int _talentId;
        private int _theftDiff;

        private class Entry
        {
            // The PICKPOCKET info's condition function, -1: the NPC has none.
            public int Condition = -1;
            public string InfoName;
            public bool IsEvaluated;
            public int Dex;
            public int Talent;
            public int Picked;
            public int Obsessed;
            public bool IsOffered;
            public int TheftDex;
            public int TheftGold;
        }

        public PickpocketDialogDomain()
        {
            // NPC containers belong to the world they were created in.
            GlobalEventDispatcher.LoadingSceneLoaded.AddListener(() => _entries.Clear());
        }

        /// <summary>
        /// The NPC's PICKPOCKET dialog option would show to the hero right now.
        /// </summary>
        public bool IsOffered(NpcContainer npc, NpcContainer hero)
        {
            var entry = Evaluate(npc, hero);
            return entry != null && entry.IsOffered;
        }

        /// <summary>
        /// "Pickpocket" chosen in the NPC's PICKPOCKET dialog: B_Beklauen with self = NPC, other = hero. True if it
        /// worked (B_Beklauen set AIV_PlayerHasPickedMyPocket), false if he was caught or nothing ran.
        /// </summary>
        public bool Steal(NpcContainer npc, NpcContainer hero)
        {
            var entry = Evaluate(npc, hero);
            var npcName = npc?.Instance?.GetName(NpcNameSlot.Slot0);
            if (entry == null || !entry.IsOffered)
            {
                Logger.LogWarning($"[Pickpocket] {npcName}: no PICKPOCKET dialog option right now - no theft.",
                    LogCat.Npc);
                return false;
            }

            var vm = _gameStateService.GothicVm;
            _theftDexGlobal.SetInt(entry.TheftDex <= _childsPlayTheftDex ? _childsPlayTheftDexGlob : entry.TheftDex, 0);
            _theftGoldGlobal.SetInt(entry.TheftGold, 0);
            try
            {
                using (new VmGlobalsScope(vm, npc.Instance, hero.Instance, null))
                    vm.Call(_stealFunctionIndex);
            }
            catch (Exception e)
            {
                Logger.LogError($"[Pickpocket] B_Beklauen on {npcName} failed: {e}", LogCat.Npc);
                return false;
            }
            finally
            {
                entry.IsEvaluated = false;
            }

            // B_GiveThiefXP changed the hero's instance - the status menu and HP bar read the vob.
            _npcService.SyncHeroInstanceToVob();
            var isSuccess = npc.Instance.GetAiVar(_pickedAivar) != 0;
            Logger.Log($"[Pickpocket] {npcName} ({entry.InfoName}): " +
                       (isSuccess
                           ? $"robbed of {entry.TheftGold} gold"
                           : $"caught the hero (DEX {entry.Dex} < {_theftDexGlobal.GetInt(0)}), B_Attack AR_Theft"),
                LogCat.Npc);
            return isSuccess;
        }

        private Entry Evaluate(NpcContainer npc, NpcContainer hero)
        {
            var vm = _gameStateService.GothicVm;
            if (npc?.Instance == null || hero?.Instance == null || !TryResolveSymbols(vm))
                return null;

            var entry = GetEntry(npc, vm);
            if (entry.Condition < 0)
                return entry;

            var dex = hero.Instance.GetAttribute(NpcAttribute.Dexterity);
            var talent = _npcHelperService.ExtNpcGetTalentSkill(hero.Instance, _talentId);
            var picked = npc.Instance.GetAiVar(_pickedAivar);
            var obsessed = _obsessed?.GetInt(0) ?? 0;
            if (entry.IsEvaluated && entry.Dex == dex && entry.Talent == talent && entry.Picked == picked &&
                entry.Obsessed == obsessed)
                return entry;

            entry.IsEvaluated = true;
            entry.Dex = dex;
            entry.Talent = talent;
            entry.Picked = picked;
            entry.Obsessed = obsessed;

            _theftDexParameter.SetInt(_marker, 0);
            _theftGoldParameter.SetInt(_marker, 0);
            int result;
            try
            {
                using (new VmGlobalsScope(vm, npc.Instance, hero.Instance, null))
                    result = vm.Call<int>(entry.Condition);
            }
            catch (Exception e)
            {
                Logger.LogError($"[Pickpocket] Condition of {entry.InfoName} failed: {e}", LogCat.Npc);
                entry.Condition = -1;
                entry.IsOffered = false;
                return entry;
            }

            var theftDex = _theftDexParameter.GetInt(0);
            if (theftDex == _marker)
            {
                Logger.LogWarning($"[Pickpocket] {entry.InfoName} doesn't call C_Beklauen - no purse for " +
                                  $"{npc.Instance.GetName(NpcNameSlot.Slot0)}.", LogCat.Npc);
                entry.Condition = -1;
                entry.IsOffered = false;
                return entry;
            }

            entry.TheftDex = theftDex;
            entry.TheftGold = _theftGoldParameter.GetInt(0);
            entry.IsOffered = result != 0;
            // Evaluated only when its inputs changed - one line per change.
            Logger.LogEditor($"[Pickpocket] {entry.InfoName} " +
                             (entry.IsOffered
                                 ? $"offered (TheftDex {entry.TheftDex}, TheftGold {entry.TheftGold}, DEX {dex})"
                                 : $"not offered: {GetReason(entry)}"), LogCat.Npc);
            return entry;
        }

        private string GetReason(Entry entry)
        {
            if (entry.Talent == 0)
                return "hero lacks NPC_TALENT_PICKPOCKET";
            if (entry.Picked != 0)
                return "already robbed (AIV_PlayerHasPickedMyPocket)";
            if (entry.Dex < entry.TheftDex - _theftDiff)
                return $"DEX {entry.Dex} < TheftDex {entry.TheftDex} - Theftdiff {_theftDiff}";
            if (entry.Obsessed != 0)
                return "NpcObsessedByDMT";
            return "condition false";
        }

        /// <summary>
        /// The NPC's info whose symbol ends with _PICKPOCKET (G2: DIA_[name]_PICKPOCKET) - looked up once per NPC.
        /// </summary>
        private Entry GetEntry(NpcContainer npc, DaedalusVm vm)
        {
            if (_entries.TryGetValue(npc, out var entry))
                return entry;

            entry = new Entry();
            var dialogs = npc.Props?.Dialogs;
            if (dialogs != null)
            {
                foreach (var info in dialogs)
                {
                    var name = vm.GetSymbolByIndex(info.Index)?.Name;
                    if (info.Condition == 0 || name == null ||
                        !name.EndsWith(_infoSuffix, StringComparison.OrdinalIgnoreCase))
                        continue;
                    entry.Condition = info.Condition;
                    entry.InfoName = name;
                    break;
                }
            }
            _entries[npc] = entry;
            if (entry.Condition < 0)
                Logger.LogEditor($"[Pickpocket] {npc.Instance.GetName(NpcNameSlot.Slot0)} has no PICKPOCKET dialog - " +
                                 "no purse", LogCat.Npc);
            return entry;
        }

        /// <summary>
        /// Once per VM (a profile switch G1 -> G2 brings a new one). False without G2's pickpocket scripts.
        /// </summary>
        private bool TryResolveSymbols(DaedalusVm vm)
        {
            if (vm == null)
                return false;
            if (vm == _vm)
                return _hasSymbols;

            _vm = vm;
            _entries.Clear();
            _theftDexParameter = vm.GetSymbolByName(_theftDexParameterName);
            _theftGoldParameter = vm.GetSymbolByName(_theftGoldParameterName);
            _theftDexGlobal = vm.GetSymbolByName(_theftDexGlobalName);
            _theftGoldGlobal = vm.GetSymbolByName(_theftGoldGlobalName);
            _obsessed = vm.GetSymbolByName(_obsessedName);
            var steal = vm.GetSymbolByName(_stealFunctionName);
            var picked = vm.GetSymbolByName(_pickedAivarName);
            var talent = vm.GetSymbolByName(_talentName);
            _theftDiff = vm.GetSymbolByName(_theftDiffName)?.GetInt(0) ?? 0;

            _hasSymbols = _theftDexParameter != null && _theftGoldParameter != null && _theftDexGlobal != null &&
                          _theftGoldGlobal != null && steal != null && picked != null && talent != null;
            if (!_hasSymbols)
            {
                Logger.LogWarning("[Pickpocket] The scripts lack C_Beklauen/B_Beklauen, TheftDexGlob/TheftGoldGlob, " +
                                  "AIV_PlayerHasPickedMyPocket or NPC_TALENT_PICKPOCKET - no G2 purses.", LogCat.Npc);
                return false;
            }

            _stealFunctionIndex = steal.Index;
            _pickedAivar = picked.GetInt(0);
            _talentId = talent.GetInt(0);
            return true;
        }
    }
}
