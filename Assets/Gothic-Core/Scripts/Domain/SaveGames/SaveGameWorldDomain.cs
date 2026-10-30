using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Proxy;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Culling;
using JetBrains.Annotations;
using Reflex.Attributes;
using UnityEngine;
using ZenKit;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.SaveGames
{
    /// <summary>
    /// Runtime state of the current world (NpcCache, VobCache, GameObjects) -> its WorldContainer -> ZenKit world
    /// written into a save game. The WorldContainer keeps the state of a world we left (revisit, later save).
    /// </summary>
    public class SaveGameWorldDomain
    {
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly NpcMeshCullingService _npcMeshCullingService;
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;

        private readonly SaveGameNpcDomain _npcDomain = new SaveGameNpcDomain().Inject();


        /// <summary>
        /// Needs to be called before we leave a world (the caches are cleared afterward) and before saving.
        /// </summary>
        /// <param name="isSaving">The world is saved (not left): the hero is part of its VOB tree then.</param>
        public void StoreWorldState(WorldContainer container, bool isSaving)
        {
            SyncItemPositions();
            StoreNpcs(container, isSaving);
        }

        /// <summary>
        /// Writes the world (from its WorldContainer) into the save game folder.
        /// </summary>
        /// <param name="hero">Set for the current world only. It's part of its VOB tree like in a G1 save game.</param>
        public bool SaveWorld(SaveGame saveGame, string path, string worldName, WorldContainer container,
            [CanBeNull] NpcContainer hero)
        {
            var world = container.SaveGameWorld ?? container.OriginalWorld;

            var rootVobs = UnwrapVobs(container.Vobs);
            if (hero != null)
            {
                hero.Vob.Position = hero.Go.transform.position.ToZkVector();
                hero.Vob.Rotation = hero.Go.transform.rotation.ToZkMatrix();
                hero.Vob.Player = true;
                _npcDomain.FlushInstanceToVob(hero);

                // We need to set Hero at the beginning of the list like in a G1 save game.
                rootVobs.Insert(0, hero.Vob.GetVob());
            }
            var npcs = container.Npcs.Select(i => (ZenKit.Vobs.Npc)i.GetVob()).ToList();

            _npcDomain.ClearPhysicsFlag(rootVobs);
            _npcDomain.ClearPhysicsFlag(npcs);

            // G2: Inventories are stored flat and restored afterward, as the running game still uses them.
            var allNpcs = rootVobs.OfType<INpc>().Concat(npcs).ToList();
            var originalInventories = allNpcs.Select(i => _npcDomain.FlattenPackedInventoryForSave(i)).ToList();
            try
            {
                world.RootObjects = rootVobs;
                world.Npcs = npcs;

                var saveWorldName = worldName.ToGothicWorldName();
                if (!saveGame.Save(path, world, saveWorldName))
                {
                    Logger.LogError($"[SaveGame] ZenKit couldn't save world {saveWorldName} into {path}.",
                        LogCat.Loading);
                    return false;
                }

                Logger.Log($"[SaveGame] World {saveWorldName}: {rootVobs.Count} root VOBs, " +
                           $"{allNpcs.Count - npcs.Count} near NPCs, {npcs.Count} far NPCs.", LogCat.Loading);
                return true;
            }
            finally
            {
                for (var i = 0; i < allNpcs.Count; i++)
                    _npcDomain.RestorePackedInventory(allNpcs[i], originalInventories[i]);
            }
        }

        /// <summary>
        /// Items are moved by physics/hands. Their VOB position is only the spawn position.
        /// </summary>
        private void SyncItemPositions()
        {
            foreach (var vobContainer in _multiTypeCacheService.VobCache)
            {
                if (vobContainer.Vob?.Type != VirtualObjectType.oCItem || vobContainer.Go == null)
                    continue;

                // The loader keeps its spawn position. The item's body is moved.
                var body = vobContainer.Go.GetComponentInChildren<Rigidbody>();
                if (body == null)
                    continue;

                vobContainer.Vob.Position = body.transform.position.ToZkVector();
                vobContainer.Vob.Rotation = body.transform.rotation.ToZkMatrix();
            }
        }

        /// <summary>
        /// The NPCs of the current world live inside NpcCache only. We store them into the world data:
        /// - NPCs close to the hero (visible) -> into the VOB tree (like a G1 save)
        /// - all other NPCs -> into the world's NPC list
        ///
        /// The hero isn't stored, as it moves with us into the next world.
        /// </summary>
        private void StoreNpcs(WorldContainer container, bool isSaving)
        {
            _npcMeshCullingService.UpdateVobPositionOfAllNpcs();
            var visibleNpcs = _npcMeshCullingService.GetVisibleNpcs().ToHashSet();

            // GlobalHero returns a new wrapper on every call: we compare the instance.
            var heroIndex = _gameStateService.GothicVm.GlobalHero?.Index;
            var storedNpcs = new HashSet<NpcContainer>();
            var nearNpcs = new List<IVirtualObject>();
            var farNpcs = new List<NpcProxy>();
            foreach (var npc in _multiTypeCacheService.NpcCache)
            {
                if (npc.Vob == null || npc.Vob.Player || npc.Instance?.Index == heroIndex)
                    continue;

                _npcDomain.FlushInstanceToVob(npc);
                npc.Vob.MarkAsRestored();
                storedNpcs.Add(npc);

                if (visibleNpcs.Contains(npc))
                    nearNpcs.Add(npc.Vob);
                else
                    farNpcs.Add(npc.Vob);
            }

            StoreTargets(storedNpcs, isSaving, heroIndex);

            // NPCs inside the tree are from the last save/visit and are replaced with the current ones.
            var vobs = container.Vobs.Where(i => i.Type != VirtualObjectType.oCNpc).ToList();
            vobs.AddRange(nearNpcs);

            container.Vobs = vobs;
            container.Npcs = farNpcs;
        }

        /// <summary>
        /// Gothic has one enemy per NPC (oCNpc enemy, set by Npc_SetTarget and Npc_GetNextEnemy), we keep the enemy
        /// (AI_Attack) and the target apart: the enemy wins. It's stored as a reference to its VOB. Only NPCs of this
        /// world's save are referenced, and the hero while saving (it's part of the VOB tree then): ZenKit would write
        /// any other VOB once more, without our fixes (ClearPhysicsFlag).
        /// </summary>
        private static void StoreTargets(HashSet<NpcContainer> storedNpcs, bool isSaving, int? heroIndex)
        {
            foreach (var npc in storedNpcs)
            {
                var target = (npc.Props.EnemyNpc ?? npc.Props.TargetNpc)?.GetUserData();
                var isReferencable = target?.Vob != null &&
                                     (storedNpcs.Contains(target) || (isSaving && target.Instance.Index == heroIndex));
                npc.Vob.Enemy = isReferencable ? target.Vob.GetVob() : null;
            }
        }

        /// <summary>
        /// Before saving the VOBs in a SaveGame, we need to unwrap our Adapters. Otherwise we get a Cast Exception
        /// from ZK C++ side.
        /// </summary>
        private static List<IVirtualObject> UnwrapVobs(List<IVirtualObject> vobs)
        {
            return vobs.Select(i => i.Type == VirtualObjectType.oCNpc ? ((NpcProxy)i).GetVob() : i).ToList();
        }
    }
}
