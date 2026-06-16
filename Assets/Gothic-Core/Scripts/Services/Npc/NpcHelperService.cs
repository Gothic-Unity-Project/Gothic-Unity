using System;
using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Adapters.Properties;
using Gothic.Core.Adapters.Properties.Vobs;
using Gothic.Core.Creator;
using Gothic.Core.Const;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Vobs;
using Gothic.Core.Extensions;
using JetBrains.Annotations;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Npc
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
        private static readonly int _raycastLayersToUse = 1 << Constants.DefaultLayer;

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
            var npcPos = npcInstance.GetUserData().Go.transform.position;

            var sensesRangeMeters = npcInstance.SensesRange / 100f;
            var sensesRangeSqr = sensesRangeMeters * sensesRangeMeters;

            var foundNpc = _multiTypeCacheService.NpcCache
                .Where(i => i.Props != null) // ignore empty (safe check)
                .Where(i => i.Go != null) // ignore empty (safe check)
                .Where(i => i.Instance.Index != npcInstance.Index) // ignore self
                .Where(i => detectPlayer ||
                            i.Instance.Index !=
                            _gameStateService.GothicVm.GlobalHero!.Index) // if we don't detect player, then skip it
                .Where(i => specificNpcIndex < 0 ||
                            specificNpcIndex == i.Instance.Index) // Specific NPC is found right now?
                .Where(i => aiState < 0 || aiState == i.Vob.CurrentStateIndex)
                .Where(i => guild < 0 || i.Instance.Guild == guild) // check guild
                .Where(i => (i.Go.transform.position - npcPos).sqrMagnitude <= sensesRangeSqr) // detect only within senses range
                .OrderBy(i => (i.Go.transform.position - npcPos).sqrMagnitude) // get nearest
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

            if (npcGo == null)
            {
                Logger.LogWarning($"ExtNpcGetDistToWp: npcGo is null for npc={npc?.GetName(NpcNameSlot.Slot0)} waypoint={waypointName}", LogCat.Npc);
                return int.MaxValue;
            }

            var waypoint = _wayNetService.GetWayNetPoint(waypointName);

            if (waypoint == null)
            {
                Logger.LogWarning($"ExtNpcGetDistToWp: waypoint '{waypointName}' not found for npc={npc?.GetName(NpcNameSlot.Slot0)}", LogCat.Npc);
                return int.MaxValue;
            }

            // *100 as Gothic metrics are in cm, not m.
            return (int)(Vector3.Distance(npcGo.transform.position, waypoint.Position) * 100);
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

        /// <summary>
        /// Senses check based on C_NPC.senses: hearing and smelling only need the range check,
        /// seeing additionally needs a free line of sight (and FOV unless freeLOS is set).
        /// </summary>
        public bool CanSenseNpc(NpcInstance self, NpcInstance other, bool freeLOS, float maxRangeMeters = float.MaxValue)
        {
            var senseRangeMeters = Mathf.Min(self.SensesRange / 100f, maxRangeMeters); // daedalus values are in cm, we need them in m
            var range = Vector3.Distance(other.GetUserData().Go.transform.position,
                self.GetUserData().Go.transform.position);

            if (range > senseRangeMeters)
            {
                return false;
            }

            var senses = (VmGothicEnums.NpcSenses)self.Senses;

            // Defensive: an NPC without configured senses keeps the previous distance-only behavior.
            if (senses == 0)
            {
                return true;
            }

            if ((senses & (VmGothicEnums.NpcSenses.Hear | VmGothicEnums.NpcSenses.Smell)) != 0)
            {
                return true;
            }

            return CanSeeNpc(self, other, freeLOS);
        }

        /// <summary>
        /// freeLOS - Free Line Of Sight == ignoreFOV
        /// fov = 50 - OpenGothic assumes 100 fov for NPCs
        /// fov = 30 - We reuse this for Focus angle during AI_Attack()
        /// </summary>
        public bool CanSeeNpc(NpcInstance self, NpcInstance other, bool freeLOS, float fov = 50f)
        {
            var selfContainer = self.GetUserData();
            var otherContainer = other.GetUserData();

            if (selfContainer == null || otherContainer == null)
            {
                return false;
            }

            // Hint: For forward direction check, we can't use HeadMesh as e.g., Molerat's head is rotated to have red axis (right) as forward.
            //       OpenGothic is therefore using Body rotation, too.
            var selfRoot = selfContainer.Go.transform;
            var otherRoot = otherContainer.Go.transform;
            var selfHead = selfContainer.PrefabProps.Head ?? selfRoot;
            var otherHead = otherContainer.PrefabProps.Head ?? otherRoot;

            var selfGroundPosition = selfRoot.position;
            var otherGroundPosition = otherRoot.position;
            // Unity places positions of objects at the bottom. We need to lift them up towards the head
            var selfRealHeadPosition = new Vector3(selfRoot.position.x, selfHead.position.y, selfRoot.position.z);
            var otherRealHeadPosition = new Vector3(otherRoot.position.x, otherHead.position.y, otherRoot.position.z);

            var distanceToNpc = Vector3.Distance(selfRealHeadPosition, otherRealHeadPosition);
            var inSightRange = distanceToNpc <= self.SensesRange / 100f; // SensesRange is in cm.

            var hasLineOfSightCollisions = Physics.Linecast(selfRealHeadPosition, otherRealHeadPosition, _raycastLayersToUse);

            // Calculate horizontal direction only (ignore Y axis for FOV check), basically a Gobbo is only using x+z for FOV and hero standing in front of it will work correctly.
            var directionToTarget = new Vector3(
                otherGroundPosition.x - selfGroundPosition.x,
                0f,
                otherGroundPosition.z - selfGroundPosition.z
            ).normalized;
            var selfForwardHorizontal = new Vector3(selfRoot.forward.x, 0f, selfRoot.forward.z).normalized;
            var angleToTarget = Vector3.Angle(selfForwardHorizontal, directionToTarget);
            var inFov = angleToTarget <= fov;

            return inSightRange && !hasLineOfSightCollisions && (freeLOS || inFov);
        }

        /// <summary>
        /// Range set via Perc_SetRange() in meters. Unset perceptions are unlimited (senses range still applies).
        /// </summary>
        public float GetPerceptionRange(VmGothicEnums.PerceptionType type)
        {
            return PerceptionRanges.TryGetValue(type, out var range) ? range : float.MaxValue;
        }
    }
}
