#if GOTHIC_HVR_INSTALLED
using Gothic.Core;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Npc;
using Gothic.VR.Adapters.Player;
using HurricaneVR.Framework.Shared;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Services
{
    /// <summary>
    /// V1 (DeveloperConfig.EnableVrTransformations): transformation scrolls (Trf_Wolf, Trf_Scavenger, ...).
    /// The spell's Spell_Logic_Trf_X calls Npc_SetActiveSpellInfo(self, monsterInstance) and returns SPL_SENDCAST - then
    /// the hero becomes that monster:
    /// - the monster is spawned like any NPC but without AI, collisions, focus or perception ("puppet"), follows the
    ///   hero's movement (walk/run/attack animations with their MDS sounds) and is invisible for the VR camera;
    /// - the VR camera goes down to the monster's eye height, the hands stay;
    /// - the hero's guild is the monster's (wolves are friendly, hunters attack) like the engine re-inits the hero;
    /// - the casting hand holds a small orb with a live picture of the puppet (the magic is still active) - its
    ///   trigger casts again = transform back.
    /// Not saved (V1): WorldSceneLoaded resets it. FIXME: save while transformed stores the monster guild.
    /// </summary>
    public class VRTransformService
    {
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly ConfigService _configService;

        private VRTransformPuppet _puppet;
        private int _originalGuild;
        private int _originalVobGuild;
        private int _originalVobGuildTrue;

        public bool IsTransformed => _puppet != null;


        public VRTransformService()
        {
            GlobalEventDispatcher.WorldSceneLoaded.AddListener(() => _puppet = null);
        }

        public void Transform(int monsterInstanceIndex, HVRHandSide castingHand)
        {
            if (!_configService.Dev.EnableVrTransformations || IsTransformed)
                return;

            var hero = _npcService.GetHeroContainer();
            if (hero?.Go == null)
                return;

            var heroPosition = Camera.main != null ? Camera.main.transform.position : hero.Go.transform.position;
            var spawnPosition = new Vector3(heroPosition.x, hero.Go.transform.position.y, heroPosition.z);
            var loaderGo = _npcService.SpawnNpcRuntime(monsterInstanceIndex, spawnPosition, Quaternion.identity);
            var monster = loaderGo != null ? loaderGo.GetComponentInChildren<Gothic.Core.Adapters.Npc.NpcLoader>(true)?.Container : null;
            if (monster == null)
            {
                Logger.LogWarning($"[VRTransform] Can't spawn the monster instance {monsterInstanceIndex}.", LogCat.VR);
                return;
            }

            // Its constructor (Set_X_Visuals) sets the model - without it the NPC can't be built (seen with the MT Lurker).
            var instanceName = loaderGo.name;
            Logger.Log($"[VRTransform] {instanceName}: visual='{monster.Props.MdsNameBase}', body='{monster.Props.MdmName}', " +
                       $"guild={monster.Instance.Guild}", LogCat.VR);
            if (string.IsNullOrEmpty(monster.Props.MdsNameBase) || string.IsNullOrEmpty(monster.Props.MdmName))
            {
                Logger.LogWarning($"[VRTransform] {instanceName} has no visual (Mdl_SetVisual/Mdl_SetVisualBody not called " +
                                  "by its constructor) - transformation cancelled.", LogCat.VR);
                loaderGo.transform.position = new Vector3(0f, -10000f, 0f);
                return;
            }

            // Like the engine (OpenGothic Npc::TransformBack): scripts see the hero as the monster.
            _originalGuild = hero.Instance.Guild;
            _originalVobGuild = hero.Vob.Guild;
            _originalVobGuildTrue = hero.Vob.GuildTrue;
            hero.Instance.Guild = monster.Instance.Guild;
            hero.Vob.Guild = monster.Instance.Guild;
            hero.Vob.GuildTrue = monster.Instance.Guild;

            _puppet = new GameObject("_TransformPuppet").AddComponent<VRTransformPuppet>();
            _puppet.Init(this, monster, loaderGo, castingHand);

            Logger.Log($"[VRTransform] Hero transformed into {monster.Instance.GetName(NpcNameSlot.Slot0)} " +
                       $"(guild {_originalGuild} -> {monster.Instance.Guild}).", LogCat.VR);
        }

        public void TransformBack()
        {
            if (!IsTransformed)
                return;

            var hero = _npcService.GetHeroContainer();
            if (hero != null)
            {
                hero.Instance.Guild = _originalGuild;
                hero.Vob.Guild = _originalVobGuild;
                hero.Vob.GuildTrue = _originalVobGuildTrue;
            }

            var puppet = _puppet;
            _puppet = null;
            puppet.Release();
            Logger.Log("[VRTransform] Hero transformed back.", LogCat.VR);
        }
    }
}
#endif
