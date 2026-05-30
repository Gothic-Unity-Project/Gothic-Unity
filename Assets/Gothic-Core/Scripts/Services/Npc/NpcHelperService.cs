using System;
using System.Collections.Generic;
using System.Linq;
using GUZ.Core.Adapters.Properties;
using GUZ.Core.Adapters.Properties.Vobs;
using GUZ.Core.Const;
using GUZ.Core.Logging;
using GUZ.Core.Creator;
using GUZ.Core.Extensions;
using GUZ.Core.Models.Container;
using GUZ.Core.Models.Vm;
using GUZ.Core.Services.Caches;
using GUZ.Core.Services.Vobs;
using JetBrains.Annotations;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = GUZ.Core.Logging.Logger;

namespace GUZ.Core.Services.Npc
{
    public class NpcHelperService
    {
        /// <summary>
        /// Ranges are in meter.
        /// FIXME - We should use PERC_ASSESSTALK range to leverage HVR's Grabbable hover and remote grab distance!
        /// </summary>
        public readonly Dictionary<VmGothicEnums.PerceptionType, int> PerceptionRanges = new ();
        
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;
        [Inject] private readonly WayNetService _wayNetService;
        [Inject] private readonly VobService _vobService;
        
        private const float _fpLookupDistance = 7f; // meter

        public void Init()
        {
            // Perceptions
            var percInitSymbol = _gameStateService.GothicVm.GetSymbolByName("InitPerceptions");
            if (percInitSymbol == null)
            {
                Logger.LogError("InitPerceptions symbol not found.", LogCat.Npc);
            }
            else
            {
                _gameStateService.GothicVm.Call(percInitSymbol.Index);
            }
        }

        public void ExtPErcSetRange(int perceptionId, int rangeInCm)
        {
            PerceptionRanges[(VmGothicEnums.PerceptionType)perceptionId] = rangeInCm / 100;
        }

        public bool ExtIsMobAvailable(NpcInstance npcInstance, string vobName)
        {
            var npc = GetNpc(npcInstance);
            var container = _vobService.GetFreeInteractableWithin10M(npc.transform.position, vobName);

            return container != null;
        }

        public int ExtWldGetMobState(NpcInstance npcInstance, string scheme)
        {
            var npcGo = GetNpc(npcInstance);

            var prefabProps = npcInstance.GetUserData().PrefabProps;

            InteractiveProperties props;

            if (prefabProps.CurrentInteractable != null)
            {
                try
                {
                    // Check current gameobject and children as well
                    props = prefabProps.CurrentInteractable.PropsAs<InteractiveProperties>();
                }
                catch (Exception)
                {
                    Logger.LogError($"Wld_GetMobState() returned an exception for {npcGo.name}", LogCat.Npc);
                    return -1;
                }
            }
            else
                props = _vobService.GetFreeInteractableWithin10M(npcGo.transform.position, scheme)?.PropsAs<InteractiveProperties>();

            if (props == null)
                return -1;

            return Math.Max(0, props.State);
        }

        public ItemInstance ExtNpcGetEquippedMeleeWeapon(NpcInstance npc)
        {
            var meleeWeapon = GetProperties(npc).EquippedItems
                .FirstOrDefault(i => i.MainFlag == (int)VmGothicEnums.ItemFlags.ItemKatNf);

            return meleeWeapon;
        }

        public bool ExtNpcHasEquippedMeleeWeapon(NpcInstance npc)
        {
            return ExtNpcGetEquippedMeleeWeapon(npc) != null;
        }

        public ItemInstance ExtNpcGetEquippedRangedWeapon(NpcInstance npc)
        {
            var rangedWeapon = GetProperties(npc).EquippedItems
                .FirstOrDefault(i => i.MainFlag == (int)VmGothicEnums.ItemFlags.ItemKatFf);

            return rangedWeapon;
        }

        public bool ExtNpcHasEquippedRangedWeapon(NpcInstance npc)
        {
            return ExtNpcGetEquippedRangedWeapon(npc) != null;
        }

        public bool ExtIsNpcOnFp(NpcInstance npc, string vobNamePart)
        {
            var freePoint = GetProperties(npc).CurrentFreePoint;

            if (freePoint == null)
            {
                return false;
            }

            return freePoint.Name.ContainsIgnoreCase(vobNamePart);
        }

        /// <summary>
        /// Returns true and sets VM.other, if NPC was found.
        ///
        /// Hint:
        /// As WldDetectNpc and WldDetectNpc seem to be the same logic except one parameter, we implement both in this function.
        /// </summary>
        public bool ExtWldDetectNpcEx(NpcInstance npcInstance, int specificNpcIndex, int aiState, int guild,
            bool detectPlayer)
        {
            var npc = GetProperties(npcInstance);
            var npcPos = npcInstance.GetUserData().Go.transform.position;
            var npcVob = npcInstance.GetUserData().Vob;

            // FIXME - add range check based on perceiveAll's range (npc.sense_range)
            var foundNpc = _multiTypeCacheService.NpcCache
                .Where(i => i.Props != null) // ignore empty (safe check)
                .Where(i => i.Go != null) // ignore empty (safe check)
                .Where(i => i.Instance.Index != npcInstance.Index) // ignore self
                .Where(i => detectPlayer ||
                            i.Instance.Index !=
                            _gameStateService.GothicVm.GlobalHero!.Index) // if we don't detect player, then skip it
                .Where(i => specificNpcIndex < 0 ||
                            specificNpcIndex == i.Instance.Index) // Specific NPC is found right now?
                .Where(i => aiState < 0 || npcVob.CurrentStateIndex == i.Vob.CurrentStateIndex)
                .Where(i => guild < 0 || i.Instance.Guild == guild) // check guild
                .OrderBy(i => Vector3.Distance(i.Go.transform.position, npcPos)) // get nearest
                .FirstOrDefault();

            // without this Dialog box stops and breaks the entire NPC logic
            if (foundNpc == null)
            {
                return false;
            }

            // We need to set it, as there are calls where we immediately need _other_. e.g.:
            // if (Wld_DetectNpc(self, ...) && (Npc_GetDistToNpc(self, other)<HAI_DIST_SMALLTALK)
            if (foundNpc.Instance != null)
            {
                _gameStateService.GothicVm.GlobalOther = foundNpc.Instance;
            }

            return foundNpc.Instance != null;
        }

        public int ExtNpcGetDistToWp(NpcInstance npc, string waypointName)
        {
            var npcGo = GetNpc(npc);
            var npcPos = npcGo.transform.position;

            var waypoint = _wayNetService.GetWayNetPoint(waypointName);

            if (waypoint == null || !npcGo)
            {
                return int.MaxValue;
            }

            // *100 as Gothic metrics are in cm, not m.
            return (int)(Vector3.Distance(npcPos, waypoint.Position) * 100);
        }

        public int ExtNpcGetTalentSkill(NpcInstance npc, int skillId)
        {
            var props = GetProperties(npc);

            // FIXME - this is related to overlays for the npc's
            return 0;
        }

        public int ExtNpcGetTalentValue(NpcInstance npc, int skillId)
        {
            return GetContainer(npc).Vob.GetTalent(skillId).Value;
        }

        public VmGothicEnums.Attitude GetPersonAttitude(NpcContainer self, NpcContainer other)
        {
            // If an NCP is checked against the player, use the temp attitude (e.g., because Hero stole something)
            if (other.PrefabProps.IsHero() && self.Vob.Attitude != self.Vob.AttitudeTemp)
                return (VmGothicEnums.Attitude)self.Vob.AttitudeTemp;

            return GetGuildAttitude(self.Vob.Guild, other.Vob.Guild);
        }

        private VmGothicEnums.Attitude GetGuildAttitude(int selfGuild, int otherGuild)
        {
            return (VmGothicEnums.Attitude)_gameStateService.GuildAttitudes[selfGuild * _gameStateService.GuildCount + otherGuild];
        }

        [CanBeNull]
        private GameObject GetNpc([CanBeNull] NpcInstance npc)
        {
            return npc.GetUserData().Go;
        }

        private NpcContainer GetContainer(NpcInstance npc)
        {
            return npc.GetUserData();
        }

        private NpcProperties GetProperties([CanBeNull] NpcInstance npc)
        {
            return npc?.GetUserData().Props;
        }

        // FIXME - CanSense is not separating between smell, hear, and see as of now. Please add functionality.
        public bool CanSenseNpc(NpcInstance self, NpcInstance other, bool freeLOS)
        {
            var senseRange = (self.SensesRange / 100); // daedalus values are in cm, we need them in m
            var range = Vector3.Distance(other.GetUserData().Go.transform.position,
                self.GetUserData().Go.transform.position);
            if (range > senseRange)
            {
                return false;
            }
            else
            {
                return true;
            }
        }
    }
}
