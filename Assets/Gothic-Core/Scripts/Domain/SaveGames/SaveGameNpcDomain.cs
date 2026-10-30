using System;
using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Models.Vob;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Context;
using Reflex.Attributes;
using ZenKit.Daedalus;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.SaveGames
{
    /// <summary>
    /// Moves NPC runtime data between NpcInstance (Daedalus' runtime truth) and the oCNpc VOB (what a save game
    /// stores), and works around ZenKit write/read asymmetries for NPC VOBs.
    /// </summary>
    public class SaveGameNpcDomain
    {
        // Original Gothic stores a fixed amount of AiVars: G1=50, G2=100.
        private const int _aiVarCountG1 = 50;
        private const int _aiVarCountG2 = 100;

        // States which don't continue after loading (upper case).
        private static readonly HashSet<string> _notContinuedStates = new() { "ZS_TALK" };

        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly ContextGameVersionService _contextGameVersionService;
        [Inject] private readonly VmCacheService _vmCacheService;

        private int AiVarCount => _contextGameVersionService.IsGothic1() ? _aiVarCountG1 : _aiVarCountG2;


        /// <summary>
        /// Copy everything Daedalus changes at runtime into the VOB, which is written into the save game.
        /// </summary>
        public void FlushInstanceToVob(NpcContainer npc)
        {
            if (npc.Instance == null || npc.Vob == null)
                return;

            npc.Vob.CopyStatsFromInstance(npc.Instance);
            FlushAiVars(npc);
            FlushEquipment(npc);
        }

        /// <summary>
        /// NPCs from a save game still need their instance constructor (name, voice, visuals, damage aren't saved).
        /// The constructor also writes into the VOB (CreateInvItems, Npc_SetTalent*, attributes), which would double
        /// inventories and reset progress. We keep the saved VOB values and copy them into the instance afterward.
        /// </summary>
        public void InitInstanceFromSave(NpcContainer npc, Action initInstance)
        {
            var vob = npc.Vob;
            SplitPackedInventoryAfterLoad(vob);

            var packed = vob.Packed.ToArray();
            var talents = vob.Talents
                .Select(i => new Talent { Type = i.Type, Value = i.Value, Skill = i.Skill })
                .ToList();
            var attributes = vob.Attributes.ToArray();
            var level = vob.Level;
            var xp = vob.Xp;
            var xpNextLevel = vob.XpNextLevel;
            var lp = vob.Lp;

            initInstance();

            for (var i = 0; i < packed.Length; i++)
                vob.SetPacked(i, packed[i]);
            vob.ClearTalents();
            talents.ForEach(i => vob.AddTalent(i));
            for (var i = 0; i < attributes.Length; i++)
                vob.SetAttribute(i, attributes[i]);
            vob.Level = level;
            vob.Xp = xp;
            vob.XpNextLevel = xpNextLevel;
            vob.Lp = lp;

            vob.CopyStatsToInstance(npc.Instance);
            RestoreAiVars(npc);

            // The constructor equipped its default items (EquipItem).
            RestoreEquipment(npc);

            // Npc_IsDead() checks the body state, which isn't part of a save game.
            if (npc.Instance.GetAttribute(NpcAttribute.HitPoints) <= 0)
                npc.Props.BodyState = VmGothicEnums.BodyState.BsDead;
            else
                ContinueSavedState(npc);
        }

        /// <summary>
        /// Like Gothic, an NPC continues a state started by AI_StartState (attacking, fleeing, unconscious, ...): it
        /// becomes the next state, which AiHandler starts first (from its start function). Afterward, the routine
        /// continues. Routine states aren't touched, the routine starts them anyway. A saved next state wins.
        /// </summary>
        private void ContinueSavedState(NpcContainer npc)
        {
            var vob = npc.Vob;
            if (vob.Player || vob.NextStateValid || !IsContinuedState(vob))
                return;

            // Symbol indices differ between installations (and script updates), names don't.
            var stateName = vob.CurrentStateName;
            var state = _gameStateService.GothicVm.GetSymbolByName(stateName);
            if (state == null)
            {
                Logger.LogWarning($"[SaveGame] State {stateName} of {vob.Name} doesn't exist in scripts. Starting " +
                                  "the routine instead.", LogCat.Loading);
                return;
            }

            vob.NextStateIndex = state.Index;
            vob.NextStateName = state.Name;
            vob.NextStateValid = true;
            vob.NextStateIsRoutine = false;
        }

        /// <summary>
        /// The current state was started by AI_StartState and continues after loading. Dialogs end with loading.
        /// </summary>
        private static bool IsContinuedState(INpc vob)
        {
            var stateName = vob.CurrentStateName;
            return vob.CurrentStateValid && !vob.CurrentStateIsRoutine && !string.IsNullOrEmpty(stateName) &&
                   !_notContinuedStates.Contains(stateName.ToUpperInvariant());
        }

        /// <summary>
        /// Once all NPCs of a restored world exist: the saved enemy (oCNpc enemy, see SaveGameWorldDomain) is a
        /// reference to the VOB of another NPC or the hero. ZenKit hands out another object for it than for the NPC
        /// lists, we therefore find the NPC by name and position. It's the target (Npc_GetTarget), and the enemy of
        /// a continued state (AI_Attack of e.g. ZS_MM_Attack needs it). A routine NPC finds its enemies itself.
        /// </summary>
        public void RestoreTargets(IReadOnlyCollection<NpcContainer> npcs)
        {
            var npcByVob = new Dictionary<(string, System.Numerics.Vector3), NpcContainer>();
            foreach (var npc in npcs)
            {
                if (npc.Vob != null)
                    npcByVob[(npc.Vob.Name, npc.Vob.Position)] = npc;
            }

            foreach (var npc in npcs)
            {
                var enemy = npc.Vob is { IsRestored: true } ? npc.Vob.Enemy : null;
                if (enemy == null || !npcByVob.TryGetValue((enemy.Name, enemy.Position), out var target))
                    continue;

                npc.Props.TargetNpc = target.Instance;
                if (IsContinuedState(npc.Vob))
                    npc.Props.EnemyNpc = target.Instance;
            }
        }

        private void FlushAiVars(NpcContainer npc)
        {
            var aiVars = new int[AiVarCount];
            for (var i = 0; i < aiVars.Length; i++)
                aiVars[i] = npc.Instance.GetAiVar(i);

            if (!npc.Vob.TrySetAiVars(aiVars))
                Logger.LogWarning($"[SaveGame] AiVars of {npc.Vob.Name} couldn't be written.", LogCat.Loading);
        }

        private void RestoreAiVars(NpcContainer npc)
        {
            var aiVars = npc.Vob.AiVars;
            var count = Math.Min(aiVars.Length, AiVarCount);
            for (var i = 0; i < count; i++)
                npc.Instance.SetAiVar(i, aiVars[i]);
        }

        /// <summary>
        /// Our inventory lives inside the packed strings. The oCNpc item list isn't used otherwise. Like original
        /// Gothic, it contains the equipped items with flag ITEM_ACTIVE.
        /// </summary>
        private void FlushEquipment(NpcContainer npc)
        {
            npc.Vob.ClearItems();
            foreach (var equippedItem in npc.Props.EquippedItems)
            {
                var itemName = _gameStateService.GothicVm.GetSymbolByIndex(equippedItem.Index)?.Name;
                if (itemName == null)
                    continue;

                npc.Vob.AddItem(new Item
                {
                    Name = itemName,
                    Instance = itemName,
                    Amount = 1,
                    Flags = (int)VmGothicEnums.ItemFlags.ItemActive,
                    PhysicsEnabled = false
                });
            }
        }

        private void RestoreEquipment(NpcContainer npc)
        {
            npc.Props.EquippedItems.Clear();
            foreach (var item in npc.Vob.Items)
            {
                if ((item.Flags & (int)VmGothicEnums.ItemFlags.ItemActive) == 0)
                    continue;

                var itemData = _vmCacheService.TryGetItemData(item.Instance);
                if (itemData == null)
                {
                    Logger.LogWarning($"[SaveGame] Equipped item {item.Instance} of {npc.Vob.Name} doesn't exist in " +
                                      "scripts. Skipping.", LogCat.Loading);
                    continue;
                }

                npc.Props.EquippedItems.Add(itemData);
            }
        }

        /// <summary>
        /// G1 (packed mode) writes the PhysicsEnabled bit, but the rigid body only if one exists. When reading, ZenKit
        /// expects a rigid body whenever the bit is set and reads garbage for every following field - a world save is
        /// unreadable then. new ZenKit.Vobs.Npc() has PhysicsEnabled=true, all VOBs from a .zen have false.
        /// We don't use the flag, therefore we always clear it.
        /// </summary>
        public void ClearPhysicsFlag(IEnumerable<IVirtualObject> vobs)
        {
            foreach (var vob in vobs)
            {
                if (vob == null)
                    continue;

                vob.PhysicsEnabled = false;
                ClearPhysicsFlag(vob.Children);
            }
        }

        /// <summary>
        /// G2's packed inventory reader only keeps the first item of the first category (it doesn't skip the ';'
        /// between categories). Comma separated items in category 0 survive. Returns the original values to restore
        /// the runtime layout after saving.
        /// </summary>
        public string[] FlattenPackedInventoryForSave(INpc vob)
        {
            if (!_contextGameVersionService.IsGothic2())
                return null;

            var original = vob.Packed.ToArray();
            vob.SetPacked(0, original.SelectMany(i => i.UnpackItems()).PackItems(','));
            for (var i = 1; i < original.Length; i++)
                vob.SetPacked(i, string.Empty);

            return original;
        }

        public void RestorePackedInventory(INpc vob, string[] original)
        {
            if (original == null)
                return;

            for (var i = 0; i < original.Length; i++)
                vob.SetPacked(i, original[i]);
        }

        /// <summary>
        /// Counterpart of FlattenPackedInventoryForSave(): Sort every item of a loaded G2 NPC back into its category.
        /// </summary>
        private void SplitPackedInventoryAfterLoad(INpc vob)
        {
            if (!_contextGameVersionService.IsGothic2())
                return;

            // Our runtime layout never uses category 0 (VmGothicEnums.InvCats starts with 1).
            var packed = vob.Packed;
            if (packed.Count == 0 || packed.Skip(1).Any(i => !string.IsNullOrEmpty(i)))
                return;

            var categories = new Dictionary<int, List<ContentItem>>();
            foreach (var item in packed[0].UnpackItems())
            {
                var itemData = _vmCacheService.TryGetItemData(item.Name);
                if (itemData == null)
                {
                    Logger.LogWarning($"[SaveGame] Item {item.Name} of {vob.Name} doesn't exist in scripts. Skipping.",
                        LogCat.Loading);
                    continue;
                }

                var category = (int)((VmGothicEnums.ItemFlags)itemData.MainFlag).ToInventoryCategory();
                if (!categories.TryGetValue(category, out var items))
                    categories[category] = items = new List<ContentItem>();
                items.Add(item);
            }

            for (var i = 0; i < packed.Count; i++)
                vob.SetPacked(i, categories.TryGetValue(i, out var items) ? items.PackItems() : string.Empty);
        }
    }
}
