using Gothic.Core;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Services;
using Gothic.Core.Services.Npc;
using Gothic.VR.Adapters.Player;
using Gothic.VR.Interaction.Models;
using Reflex.Attributes;
using UnityEngine;
using ZenKit;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Services
{
    /// <summary>
    /// Transformation scrolls (Trf_Wolf, Trf_Scavenger, ...).
    /// The spell's Spell_Logic_Trf_X calls Npc_SetActiveSpellInfo(self, monsterInstance) and returns SPL_SENDCAST - then
    /// the hero becomes that monster:
    /// - the monster is spawned like any NPC but without AI, collisions, focus or perception ("puppet"), follows the
    ///   hero's movement (walk/run/attack animations with their MDS sounds) and is invisible for the VR camera;
    /// - the VR camera goes down to the monster's eye height, the hands stay;
    /// - the hero's guild is the monster's (wolves are friendly, hunters attack) - Core: NpcService.TransformHero;
    /// - the casting hand holds a small orb with a live picture of the puppet (the magic is still active) - its
    ///   trigger casts again = transform back.
    /// Not saved (V1): WorldSceneLoaded resets it. FIXME: save while transformed stores the monster guild.
    /// Lives in the VR scene scope: Reflex disposes it with the scene, which removes the listeners from the static
    /// events - a reloaded VR scene would transform back twice otherwise.
    /// </summary>
    public class VRSpellTransformService : System.IDisposable
    {
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly GameStateService _gameStateService;

        private const string _transformSpellPrefix = "SPL_TRF_";

        private VRSpellTransformPuppet _puppet;

        // Not the puppet's Unity null: it's destroyed with the world scene, the guild must still come back.
        private bool _isTransformed;
        public bool IsTransformed => _isTransformed;


        public VRSpellTransformService()
        {
            // Leaving the world transformed (level change, loading a save) kept the monster's guild for good -
            // camps attacked - and the rig's camera height, speed and no-grab.
            GlobalEventDispatcher.LoadingSceneLoaded.AddListener(TransformBack);
            GlobalEventDispatcher.WorldSceneLoaded.AddListener(TransformBack);
        }

        public void Dispose()
        {
            GlobalEventDispatcher.LoadingSceneLoaded.RemoveListener(TransformBack);
            GlobalEventDispatcher.WorldSceneLoaded.RemoveListener(TransformBack);
        }

        /// <returns>False if the hero wasn't transformed (the scroll isn't used up then).</returns>
        public bool Transform(int monsterInstanceIndex, VrHandSide castingHand)
        {
            if (IsTransformed)
                return false;

            var hero = _npcService.GetHeroContainer();
            if (hero?.Go == null)
                return false;

            var head = _vrPlayerService.Rig?.Head;
            var heroPosition = head != null ? head.position : hero.Go.transform.position;
            var spawnPosition = new Vector3(heroPosition.x, hero.Go.transform.position.y, heroPosition.z);
            var loaderGo = _npcService.SpawnNpcRuntime(monsterInstanceIndex, spawnPosition, Quaternion.identity);
            var monster = loaderGo != null ? loaderGo.GetComponentInChildren<Gothic.Core.Adapters.Npc.NpcLoader>(true)?.Container : null;
            if (monster == null)
            {
                Logger.LogWarning($"[VRTransform] Can't spawn the monster instance {monsterInstanceIndex}.", LogCat.VR);
                return false;
            }

            // Its constructor (Set_X_Visuals) sets the model - without it the NPC can't be built (Mroczne Tajemnice's
            // Spell_Logic_Trf_Lurker passes BLACKDRAGON, an instance without a visual).
            var instanceName = loaderGo.name;
            Logger.Log($"[VRTransform] {instanceName}: visual='{monster.Props.MdsNameBase}', body='{monster.Props.MdmName}', " +
                       $"guild={monster.Instance.Guild}", LogCat.VR);
            if (string.IsNullOrEmpty(monster.Props.MdsNameBase) || string.IsNullOrEmpty(monster.Props.MdmName))
            {
                Logger.LogWarning($"[VRTransform] {instanceName} has no visual (Mdl_SetVisual/Mdl_SetVisualBody not called " +
                                  "by its constructor) - transformation cancelled.", LogCat.VR);
                _npcService.DespawnRuntimeNpc(monster);
                return false;
            }

            // Scripts see the hero as the monster (its guild) - Core's rule, the puppet and camera are VR's.
            _npcService.TransformHero(monster);

            _puppet = new GameObject("_TransformPuppet").AddComponent<VRSpellTransformPuppet>();
            _isTransformed = true;
            _puppet.Init(this, monster, castingHand);

            Logger.Log($"[VRTransform] Hero transformed into {monster.Instance.GetName(NpcNameSlot.Slot0)}.",
                LogCat.VR);
            return true;
        }

        /// <summary>
        /// The monster the spell is named after - SPL_TRF_LURKER (the
        /// spell's id) -> instance LURKER. For a script that passes a monster without a visual (Mroczne Tajemnice's
        /// lurker scroll passes BLACKDRAGON). -1 if there's none.
        /// </summary>
        public int GetSpellNamedMonster(int spellId)
        {
            var vm = _gameStateService.GothicVm;
            if (vm == null || spellId < 0)
                return -1;
            for (var i = 0; i < vm.SymbolCount; i++)
            {
                var symbol = vm.GetSymbolByIndex(i);
                if (symbol == null || !symbol.IsConst || symbol.Type != DaedalusDataType.Int ||
                    !symbol.Name.StartsWith(_transformSpellPrefix, System.StringComparison.Ordinal) ||
                    symbol.GetInt(0) != spellId)
                    continue;
                var monster = vm.GetSymbolByName(symbol.Name.Substring(_transformSpellPrefix.Length));
                return monster != null && monster.Type == DaedalusDataType.Instance ? monster.Index : -1;
            }
            return -1;
        }

        public void TransformBack()
        {
            if (!IsTransformed)
                return;

            _npcService.TransformHeroBack();
            _isTransformed = false;
            var puppet = _puppet;
            _puppet = null;
            if (puppet != null)
                puppet.Release();
            Logger.Log("[VRTransform] Hero transformed back.", LogCat.VR);
        }
    }
}
