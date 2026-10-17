using System;
using System.Collections.Generic;
using Gothic.Core.Adapters.Properties;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Const;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Extensions;
using Gothic.Core.Models.Caches;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Models.Vob.WayNet;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Culling;
using Gothic.Core.Services.Meshes;
using Gothic.Core.Services.StaticCache;
using Gothic.Core.Services.World;
using JetBrains.Annotations;
using MyBox;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using ZenKit.Util;
using ZenKit.Vobs;
using Light = ZenKit.Vobs.Light;
using LightType = ZenKit.Vobs.LightType;
using Logger = Gothic.Core.Logging.Logger;
using Object = UnityEngine.Object;

namespace Gothic.Core.Domain.Vobs
{
    /// <summary>
    /// Outsourced logic to create actual VOB GO structures.
    /// </summary>
    public class VobInitializerDomain
    {
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly VobSoundCullingService _vobSoundCullingService;
        [Inject] private readonly MeshService _meshService;
        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly StaticCacheService _staticCacheService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;
        [Inject] private readonly ContextGameVersionService _contextGameVersionService;
        [Inject] private readonly StationaryLightsService _stationaryLightsService;


        /// <summary>
        /// Generic root method to create a VOB of any type.
        /// Type specific creation will be handled inside further Create*() methods.
        ///
        /// parentWorldPosition - When we load Lights, we need to assign the shader's Index. As lazy loading will randomly load them,
        ///                       and Lights could be sub-VOBs inside fire, we need to find the index from WorldPosition calculated at
        ///                       caching times. Same calculation! (ZenKit.IVirtualVob.Position + ZK.parentPos
        /// isInRelativeTree - VOBs of a fire's own .zen: positions relative to the fire. In a world, every VOB's position
        ///                    is a world position (ZEN: trafoOSToWSPos), children too.
        /// </summary>
        public void InitVob(IVirtualObject vob, GameObject parent, Vector3 parentWorldPosition, bool isRootVob,
            bool isInRelativeTree = false)
        {
            // Light indices match the static cache, which adds the positions up the same way - keep it for lights.
            var worldPosition = parentWorldPosition + vob.Position.ToUnityVector();
            // DeveloperConfig.EnableWorldSpaceChildVobs: where the VOB really is (free points NPCs walk to).
            var placementPosition = _configService.Dev.EnableWorldSpaceChildVobs && !isInRelativeTree
                ? vob.Position.ToUnityVector()
                : worldPosition;
            GameObject go = null;

            switch (vob.Type)
            {
                case VirtualObjectType.oCItem:
                    go = CreateItem((Item)vob, parent);
                    break;
                case VirtualObjectType.oCMobContainer:
                    go = CreateMobContainer((Container)vob, parent);
                    break;
                case VirtualObjectType.zCVobSound:
                    if (_configService.Dev.EnableGameSounds)
                    {
                        go = CreateSound((Sound)vob, parent);
                    }

                    break;
                case VirtualObjectType.zCVobSoundDaytime:
                    if (_configService.Dev.EnableGameSounds)
                    {
                        go = CreateSoundDaytime((SoundDaytime)vob, parent);
                    }

                    break;
                case VirtualObjectType.oCZoneMusic:
                case VirtualObjectType.oCZoneMusicDefault:
                    go = CreateZoneMusic((ZoneMusic)vob, parent);
                    break;
                case VirtualObjectType.zCVobSpot:
                case VirtualObjectType.zCVobStartpoint:
                    go = CreateSpot(vob, parent, placementPosition, _configService.Dev.ShowFreePoints);
                    break;
                case VirtualObjectType.oCTriggerChangeLevel:
                    go = CreateTriggerChangeLevel((TriggerChangeLevel)vob, parent);
                    break;
                case VirtualObjectType.zCVob:
                    if (vob.Visual == null)
                    {
                        CreateDebugObject(vob, parent);
                        break;
                    }

                    switch (vob.Visual!.Type)
                    {
                        case VisualType.Decal:
                            if (_configService.Dev.EnableDecalVisuals)
                            {
                                go = CreateDecal(vob, parent);
                            }

                            break;
                        case VisualType.ParticleEffect:
                            if (_configService.Dev.EnableParticleEffects)
                            {
                                go = CreatePfx(vob, parent, isInRelativeTree);
                            }

                            break;
                        default:
                            go = CreateDefaultMesh(vob, parent);
                            break;
                    }
                    break;
                case VirtualObjectType.oCMobInter:
                    if (vob.Name.ContainsIgnoreCase("bench") ||
                        vob.Name.ContainsIgnoreCase("chair") ||
                        vob.Name.ContainsIgnoreCase("throne") ||
                        vob.Name.ContainsIgnoreCase("barrelo"))
                    {
                        // go = CreateSeat(vob, parent);
                        go = CreateDefaultMesh(vob, parent);
                        break;
                    }

                    go = CreateDefaultMesh(vob, parent);
                    break;
                case VirtualObjectType.oCMobDoor:
                    go = CreateDefaultMesh(vob, parent);
                    break;
                case VirtualObjectType.oCMobSwitch:
                case VirtualObjectType.oCMOB:
                case VirtualObjectType.zCVobStair:
                case VirtualObjectType.oCMobBed:
                case VirtualObjectType.oCMobWheel:
                    go = CreateDefaultMesh(vob, parent);
                    break;
                case VirtualObjectType.zCVobAnimate:
                    go = CreateAnimatedVob((Animate)vob, parent);
                    break;
                case VirtualObjectType.oCMobFire:
                    go = CreateFire((Fire)vob, parent, worldPosition);
                    break;
                case VirtualObjectType.zCVobLight:
                    go = CreateLight((Light)vob, parent, worldPosition);
                    break;
                case VirtualObjectType.oCMobLadder:
                    go = CreateDefaultMesh(vob, parent);
                    break;
                case VirtualObjectType.zCMover:
                    go = CreateMover((IMover)vob, parent);
                    TryAddDamageTargetMarker(vob, go);
                    RegisterChildLogicVob(vob, go, isRootVob);
                    break;
                case VirtualObjectType.zCPFXController:
                    // DeveloperConfig.EnableParticleEffects: a running controller shows its effect (sparks over the
                    // pentagram, Xardas's tower). Controllers started by a trigger aren't handled yet.
                    if (_configService.Dev.EnableParticleEffects && vob is IParticleEffectController pfxController &&
                        pfxController.InitiallyRunning && !string.IsNullOrEmpty(GetPfxControllerEffect(pfxController)))
                    {
                        go = _meshService.CreateVobPfx(GetPfxControllerEffect(pfxController),
                            GetPfxPosition(vob, parent, isInRelativeTree), vob.Rotation.ToUnityQuaternion(), parent);
                        break;
                    }

                    // Diagnostics: controllers started later by a trigger (not handled yet) - which effect, where.
                    if (vob is IParticleEffectController waitingController)
                        Logger.Log($"[PFX] Controller '{vob.Name}' ({GetPfxControllerEffect(waitingController)}) at " +
                                   $"{vob.Position.ToUnityVector()} waits for a trigger - not shown", LogCat.Vob);

                    // A Particle controller makes no sense without a visual to show.
                    // Therefore, removing it now (as it's also not included in official G1 saves, and not visible within Spacer)
                    if (!vob.ShowVisual)
                    {
                        break;
                    }

                    // For SaveGame comparison, we load our fallback Prefab and set VobProperties.
                    // Remove it from here once we properly implement and handle it.
                    go = CreateEmptyDefaultVob(vob, parent);
                    break;
                case VirtualObjectType.zCTriggerList:
                    // This value is always true when a new game/world is loaded. (Compared with G1 save game.)
                    ((TriggerList)vob).SendOnTrigger = true;

                    go = CreateTriggerList((ITriggerList)vob, parent);
                    FinishChildLogicVob(vob, go, parent, worldPosition, isRootVob, isInRelativeTree);
                    return;
                case VirtualObjectType.oCTriggerScript:
                    go = CreateTriggerScript((ITriggerScript)vob, parent);
                    TryAddDamageTargetMarker(vob, go);
                    FinishChildLogicVob(vob, go, parent, worldPosition, isRootVob, isInRelativeTree);
                    return;
                case VirtualObjectType.zCCodeMaster:
                    go = CreateCodeMaster((ICodeMaster)vob, parent);
                    FinishChildLogicVob(vob, go, parent, worldPosition, isRootVob, isInRelativeTree);
                    return;
                case VirtualObjectType.zCTrigger:
                    go = CreateTriggerZone((ITrigger)vob, parent);
                    TryAddDamageTargetMarker(vob, go);
                    FinishChildLogicVob(vob, go, parent, worldPosition, isRootVob, isInRelativeTree);
                    return;
                case VirtualObjectType.zCTriggerUntouch:
                    go = CreateTriggerUntouch((ITriggerUntouch)vob, parent);
                    return;
                case VirtualObjectType.zCTriggerWorldStart:
                    go = CreateTriggerWorldStart((ITriggerWorldStart)vob, parent);
                    return;
                case VirtualObjectType.zCMoverController:
                    go = CreateMoverController((IMoverController)vob, parent);
                    FinishChildLogicVob(vob, go, parent, worldPosition, isRootVob, isInRelativeTree);
                    return;
                case VirtualObjectType.zCMessageFilter:
                    go = CreateMessageFilter((IMessageFilter)vob, parent);
                    FinishChildLogicVob(vob, go, parent, worldPosition, isRootVob, isInRelativeTree);
                    return;
                case VirtualObjectType.zCVobScreenFX:
                case VirtualObjectType.oCCSTrigger:
                case VirtualObjectType.zCVobLensFlare:
                case VirtualObjectType.zCZoneZFog:
                case VirtualObjectType.zCZoneZFogDefault:
                case VirtualObjectType.zCZoneVobFarPlane:
                case VirtualObjectType.zCZoneVobFarPlaneDefault:
                case VirtualObjectType.zCCSCamera:
                case VirtualObjectType.zCCamTrj_KeyFrame:
                case VirtualObjectType.oCTouchDamage:
                case VirtualObjectType.zCEarthquake:
                case VirtualObjectType.Ignored:
                case VirtualObjectType.Unknown:
                    // For SaveGame comparison, we load our fallback Prefab and set VobProperties.
                    // Remove it from here once we properly implement and handle it.
                    CreateEmptyDefaultVob(vob, parent);
                    // DeveloperConfig.EnableChildLogicVobs: their children (a cutscene camera's trigger lists moving
                    // gates/pillars) are created anyway. Root ones: VobService made the children roots already.
                    if (_configService.Dev.EnableChildLogicVobs && !isRootVob)
                    {
                        foreach (var childVob in vob.Children)
                            InitVob(childVob, parent, worldPosition, false, isInRelativeTree);
                    }

                    return;
                case VirtualObjectType.zCVobLevelCompo:
                    // Nothing to do.
                    return;
                default:
                    Logger.LogError($"VobType={vob.Type} not yet handled. And we didn't know we need to do so. ;-)", LogCat.Vob);
                    return;
            }
            // A decal/particle VOB skipped because those visuals are off (EnableDecalVisuals/EnableParticleEffects, off in
            // Production) still carries its children: an empty GO holds them. In builds G2's Irdorath trap/bridge
            // triggers (children of particle VOBs) were never created - "chained target ... not found", nothing to shoot.
            if (!go && vob.Children.Count > 0 && IsVisualDisabled(vob))
            {
                go = new GameObject($"{vob.Name} (visual off)");
                go.transform.SetParent(parent != null ? parent.transform : null, false);
                go.transform.SetPositionAndRotation(placementPosition, vob.Rotation.ToUnityQuaternion());
            }

            // Do not check children if the current VOB can't be created.
            if (!go)
                return;

            // DeveloperConfig.EnableWorldSpaceChildVobs: a child VOB stayed at its parent's origin - only the root's
            // loader is placed. Hidden in vanilla (children mostly sit at their parent), but The Chronicles Of Myrtana's
            // intro ship had all its crates, hammocks and free points in one pile.
            if (!isRootVob && !isInRelativeTree && _configService.Dev.EnableWorldSpaceChildVobs)
                go.transform.SetPositionAndRotation(vob.Position.ToUnityVector(), vob.Rotation.ToUnityQuaternion());

            if (!isRootVob)
                EnsureOwnVobLoader(vob, go);

            // DeveloperConfig.EnableVobWorldLight: static VOBs in caves/houses take the light of the floor below.
            if (vob.Type is not (VirtualObjectType.oCItem or VirtualObjectType.zCVobLight or VirtualObjectType.zCMover or
                VirtualObjectType.zCPFXController))
                _stationaryLightsService.ApplyWorldLight(go);

            foreach (var childVob in vob.Children)
            {
                InitVob(childVob, go, worldPosition, false, isInRelativeTree);
            }
        }

        /// <summary>
        /// DeveloperConfig.EnableChildLogicVobs: a trigger/trigger list/code master/... below another VOB - registered
        /// like a root one, and its own children created (the trigger cases returned before them).
        /// </summary>
        private void FinishChildLogicVob(IVirtualObject vob, GameObject go, GameObject parent, Vector3 worldPosition,
            bool isRootVob, bool isInRelativeTree)
        {
            RegisterChildLogicVob(vob, go, isRootVob);
            if (!_configService.Dev.EnableChildLogicVobs || go == null)
                return;

            // Not below go: trigger zones are scaled to their bounding box.
            foreach (var childVob in vob.Children)
                InitVob(childVob, parent, worldPosition, false, isInRelativeTree);
        }

        /// <summary>
        /// Triggers, trigger lists, code masters, movers, ... are only registered (VobService.AddToMobInteractableList)
        /// when they are root VOBs. As children of another VOB nobody found them - G2 Irdorath's last door: the Innos
        /// statue sent 'EVT_FINAL_DOOR_TRIGGERLIST' ... "not found", the door stayed shut. Same lookup keys as for roots.
        /// </summary>
        private void RegisterChildLogicVob(IVirtualObject vob, GameObject go, bool isRootVob)
        {
            if (isRootVob || go == null || !_configService.Dev.EnableChildLogicVobs)
                return;

            var name = vob.Name;
            if (string.IsNullOrEmpty(name) && vob.Type == VirtualObjectType.oCTriggerScript)
                name = vob.GetVisualName();
            if (string.IsNullOrEmpty(name))
                return;

            var key = name.ToUpper();
            var container = new VobContainer(vob) { Go = go };
            switch (vob.Type)
            {
                case VirtualObjectType.zCMover:
                    if (!_gameStateService.VobsMover.TryGetValue(key, out var movers))
                        _gameStateService.VobsMover[key] = movers = new();
                    movers.RemoveAll(m => m?.Go == null || !m.Go);
                    movers.Add(container);
                    break;
                case VirtualObjectType.oCTriggerScript:
                    AddLogicVob(_gameStateService.VobsTriggerScript, key, container);
                    break;
                case VirtualObjectType.zCTriggerList:
                    AddLogicVob(_gameStateService.VobsTriggerList, key, container);
                    break;
                case VirtualObjectType.zCCodeMaster:
                    AddLogicVob(_gameStateService.VobsCodeMaster, key, container);
                    break;
                case VirtualObjectType.zCTrigger:
                    AddLogicVob(_gameStateService.VobsTrigger, key, container);
                    break;
                case VirtualObjectType.zCMoverController:
                    AddLogicVob(_gameStateService.VobsMoverController, key, container);
                    break;
                case VirtualObjectType.zCMessageFilter:
                    AddLogicVob(_gameStateService.VobsMessageFilter, key, container);
                    break;
                default:
                    return;
            }

            Logger.Log($"[ChildVob] Registered child {vob.Type} '{key}'", LogCat.Vob);
        }

        /// <summary>
        /// The first one of a name wins, but an entry whose GameObject is gone (the world was loaded before - loading a
        /// save in game) is replaced. With TryAdd the stale entry stayed: G2 Irdorath's gate levers sent their
        /// '..._MSG' after a reload and it was "not found", the wheels and gates didn't move until a restart.
        /// </summary>
        private static void AddLogicVob(Dictionary<string, VobContainer> vobs, string key, VobContainer container)
        {
            if (vobs.TryGetValue(key, out var existing) && existing?.Go != null && existing.Go)
                return;
            vobs[key] = container;
        }
        private bool IsVisualDisabled(IVirtualObject vob)
        {
            return vob.Type == VirtualObjectType.zCVob && vob.Visual?.Type switch
            {
                VisualType.Decal => !_configService.Dev.EnableDecalVisuals,
                VisualType.ParticleEffect => !_configService.Dev.EnableParticleEffects,
                _ => false
            };
        }


        /// <summary>
        /// DeveloperConfig.EnableChildVobLoaders: child vobs are created inside their parent's GO without a VobLoader of
        /// their own. Their adapters (VRVobContainer, VRVobContainerPhysicsChest, door lock picking, VRFocus) call
        /// GetComponentInParent<VobLoader>() and got the PARENT's vob (often an unnamed zCVob) - InvalidCastExceptions,
        /// "No door or container found for ><", doors/chests that don't work (G2 Xardas' tower).
        /// </summary>
        private void EnsureOwnVobLoader(IVirtualObject vob, GameObject go)
        {
            if (!_configService.Dev.EnableChildVobLoaders || vob is not (IInteractiveObject or IItem))
                return;

            var loader = go.GetComponentInParent<VobLoader>();
            if (loader != null && loader.Container?.Vob == vob)
                return;

            var ownLoader = go.AddComponent<VobLoader>();
            ownLoader.Container = new VobContainer(vob) { Go = go };
            ownLoader.IsLoaded = true;
            Logger.Log($"[ChildVob] '{vob.Name}' ({vob.Type}) under '{loader?.Container?.Vob?.Name}' got its own VobLoader.", LogCat.Vob);
        }

        public void SetPosAndRot(GameObject obj, System.Numerics.Vector3 position, Matrix3x3 rotation)
        {
            obj.transform.SetLocalPositionAndRotation(position.ToUnityVector(), rotation.ToUnityQuaternion());
        }

        private GameObject GetPrefab(IVirtualObject vob, GameObject parent = null)
        {
            GameObject go;
            var name = vob.Name;

            switch (vob.Type)
            {
                case VirtualObjectType.oCItem:
                    var vobItem = (IItem)vob;
                    var itemName = vobItem.Instance.NotNullOrEmpty() ? vobItem.Instance : vobItem.Name;
                    var item = _vmCacheService.TryGetItemData(itemName)!;
                    var mainFlag = (VmGothicEnums.ItemFlags)item.MainFlag;
                    
                    if (mainFlag is VmGothicEnums.ItemFlags.ItemKatNf or VmGothicEnums.ItemFlags.ItemKatFf)
                        go = _resourceCacheService.TryGetPrefabObject(PrefabType.VobItemWeapon, name: name, parent: parent);
                    else if (name.EqualsIgnoreCase("ItKeLockpick") || name.EqualsIgnoreCase("ItKe_Lockpick") ||
                             itemName.EqualsIgnoreCase("ItKeLockpick") || itemName.EqualsIgnoreCase("ItKe_Lockpick"))
                        go = _resourceCacheService.TryGetPrefabObject(PrefabType.VobItemLockPick, name: name, parent: parent);
                    else
                        go = _resourceCacheService.TryGetPrefabObject(PrefabType.VobItem, name: name, parent: parent);

                    break;
                case VirtualObjectType.zCVobSpot:
                case VirtualObjectType.zCVobStartpoint:
                    go = _resourceCacheService.TryGetPrefabObject(PrefabType.VobSpot, name: name, parent: parent);
                    break;
                case VirtualObjectType.zCVobSound:
                    go = _resourceCacheService.TryGetPrefabObject(PrefabType.VobSound, name: name, parent: parent);
                    break;
                case VirtualObjectType.zCVobSoundDaytime:
                    go = _resourceCacheService.TryGetPrefabObject(PrefabType.VobSoundDaytime, name: name, parent: parent);
                    break;
                case VirtualObjectType.oCZoneMusic:
                case VirtualObjectType.oCZoneMusicDefault:
                    go = _resourceCacheService.TryGetPrefabObject(PrefabType.VobMusic, name: name, parent: parent);
                    break;
                case VirtualObjectType.oCMOB:
                    go = _resourceCacheService.TryGetPrefabObject(PrefabType.Vob, name: name, parent: parent);
                    break;
                case VirtualObjectType.oCMobFire:
                    go = _resourceCacheService.TryGetPrefabObject(PrefabType.VobFire, name: name, parent: parent);
                    break;
                case VirtualObjectType.oCMobInter:
                    if (vob.Name.ContainsIgnoreCase("bench") ||
                        vob.Name.ContainsIgnoreCase("chair") ||
                        vob.Name.ContainsIgnoreCase("throne") ||
                        vob.Name.ContainsIgnoreCase("barrelo"))
                    {
                        go = _resourceCacheService.TryGetPrefabObject(PrefabType.VobInteractableSeat);
                    }
                    else
                    {
                        go = _resourceCacheService.TryGetPrefabObject(PrefabType.VobInteractable);
                    }
                    break;
                case VirtualObjectType.oCMobBed:
                    go = _resourceCacheService.TryGetPrefabObject(PrefabType.VobBed, name: name, parent: parent);
                    break;
                case VirtualObjectType.oCMobWheel:
                    go = _resourceCacheService.TryGetPrefabObject(PrefabType.VobWheel, name: name, parent: parent);
                    break;
                case VirtualObjectType.oCMobSwitch:
                    go = _resourceCacheService.TryGetPrefabObject(PrefabType.VobSwitch, name: name, parent: parent);
                    break;
                case VirtualObjectType.oCMobDoor:
                    var visualName = vob.Visual?.Name;
                    // G2 beds are named BEDHIGH_NW_..., not only BED_... (they were built as doors - no focus, no use).
                    var isBed = !visualName.IsNullOrEmpty()
                                && visualName.StartsWithIgnoreCase("BED");
                    go = isBed
                        ? _resourceCacheService.TryGetPrefabObject(PrefabType.VobBed, name: name, parent: parent)
                        : _resourceCacheService.TryGetPrefabObject(PrefabType.VobDoor, name: name, parent: parent);
                    break;
                case VirtualObjectType.oCMobContainer:
                    // A container without a lid (no .MDS, e.g. the orc mummies CHESTBIG_ORCMUMMY.ASC) is set up like
                    // other mobs: highlight + click - the click opens a loot backpack (VRPlayerService.HandleMobGrab).
                    var containerVisual = vob.Visual?.Name;
                    var hasLid = containerVisual.IsNullOrEmpty() || containerVisual.EndsWithIgnoreCase(".MDS");
                    go = _resourceCacheService.TryGetPrefabObject(
                        hasLid ? PrefabType.VobContainer : PrefabType.VobInteractable, name: name, parent: parent);
                    break;
                case VirtualObjectType.oCMobLadder:
                    go = _resourceCacheService.TryGetPrefabObject(PrefabType.VobLadder, name: name, parent: parent);
                    break;
                case VirtualObjectType.zCVobAnimate:
                    go = _resourceCacheService.TryGetPrefabObject(PrefabType.VobAnimate, name: name, parent: parent);
                    break;
                case VirtualObjectType.zCVobLight:
                    go = _resourceCacheService.TryGetPrefabObject(PrefabType.VobLight, name: name, parent: parent);
                    break;
                case VirtualObjectType.oCTriggerChangeLevel:
                    go = _resourceCacheService.TryGetPrefabObject(PrefabType.VobTriggerChangeLevel, name: name, parent: parent);
                    break;
                case VirtualObjectType.oCTriggerScript:
                    go = _resourceCacheService.TryGetPrefabObject(PrefabType.VobTriggerScript, name: name, parent: parent);
                    break;
                default:
                    go = _resourceCacheService.TryGetPrefabObject(PrefabType.Vob, name: name, parent: parent);
                    break;
            }

            return go;
        }

        /// <summary>
        /// Create nothing than an empty Prefab GO with VobProperties.
        /// Needed for proper SaveGame comparison (VOB counts).
        ///
        /// FIXME - As we have Lazy Loading now, we can remove this one and simply collect all Lazy.Components and other objects during save.
        /// </summary>
        private GameObject CreateEmptyDefaultVob(IVirtualObject vob, GameObject parent)
        {
            var go = GetPrefab(vob);

            go.SetParent(parent);

            return go;
        }

        /// <summary>
        /// Some fire slots have the light too low to cast light onto the mesh and the surroundings.
        /// </summary>
        [CanBeNull]
        private GameObject CreateFire(Fire vob, GameObject parent, Vector3 worldPosition)
        {
            var go = CreateDefaultMesh(vob, parent);

            // DeveloperConfig.EnableFireVobChildren: a fireplace without a fire tree is still a mesh - returning null
            // dropped its child VOBs. Mroczne Tajemnice's smithy on the smugglers' beach (a high fireplace below
            // another one) was missing.
            var meshOnly = _configService.Dev.EnableFireVobChildren ? go : null;

            if (vob.VobTree == "")
            {
                return meshOnly;
            }

            var fireWorld = _resourceCacheService.TryGetWorld(vob.VobTree, _contextGameVersionService.Version, true);

            if (fireWorld == null)
            {
                Logger.LogWarning($"Fire world {vob.VobTree} not found. Skipping usage...", LogCat.Loading);
                return meshOnly;
            }
            
            var slot = go.FindChildRecursively(vob.Slot) ?? go;
            var childCountBefore = slot.transform.childCount;
            CreateFireVobs(fireWorld.RootObjects, slot, worldPosition);

            // DeveloperConfig.EnableFireStates: the fire tree burns by the scheme's mob routine (lit at night), always
            // indoors (light mapped: houses, mines), can be toggled and is off when far away.
            if (_configService.Dev.EnableFireStates)
            {
                var fireObjects = new List<GameObject>();
                for (var i = childCountBefore; i < slot.transform.childCount; i++)
                    fireObjects.Add(slot.transform.GetChild(i).gameObject);
                var scheme = vob.Visual?.Name?.Split('_')[0];
                var isIndoor = _stationaryLightsService.IsNearLightMappedPolygons(worldPosition, _fireIndoorRadius);
                go.AddComponent<VobFireHandler>().Init(scheme, isIndoor, fireObjects);
            }

            return go;
        }

        /// <summary>
        /// A fire itself is a .zen file which contains children.
        /// </summary>
        private void CreateFireVobs(List<IVirtualObject> vobs, GameObject parent, Vector3 worldPosition)
        {
            foreach (var vob in vobs)
            {
                // FIRE worlds aren't positioned at 0,0,0. We need to do it now, to have the correct parent-child positioning.
                vob.Position = default;

                // Call normal mesh and Prefab loading logic
                InitVob(vob, parent, worldPosition, false, true);
            }
        }

        // A fire this close to light mapped polygons (houses, caves, mines) is indoors - it always burns.
        private const float _fireIndoorRadius = 3f;

        private GameObject CreateLight(Light vob, GameObject parent, Vector3 worldPosition)
        {
            // Static lights are baked into the world's vertex light outdoors. In light mapped areas (caves, houses)
            // they light the walls only via lightmaps we don't render - there they get a runtime light slot.
            var isRuntimeSlot = false;
            if (vob.LightStatic)
            {
                if (!_configService.Dev.EnableStaticLightsInLightMappedAreas ||
                    !_stationaryLightsService.IsNearLightMappedPolygons(worldPosition, vob.Range * .01f))
                    return null;
                isRuntimeSlot = true;
            }

            var go = GetPrefab(vob);
            go.name = $"{vob.LightType} Light {vob.Name}";

            // TODO - We need to be careful. It might be, that a light is in a sub-GO structure, where we need the parent pos+rot. If it's the case, we will get a warning as Index==-1 below.
            go.SetParent(parent, true, true);

            var lightComp = go.GetComponent<StationaryLight>();
            lightComp.Color = new Color(vob.Color.R / 255f, vob.Color.G / 255f, vob.Color.B / 255f, vob.Color.A / 255f);
            lightComp.Type = vob.LightType == LightType.Point
                ? UnityEngine.LightType.Point
                : UnityEngine.LightType.Spot;
            lightComp.Range = vob.Range * .01f;
            lightComp.SpotAngle = vob.ConeAngle;
            lightComp.Intensity = 1;

            // DeveloperConfig.EnableWorldLightmaps: a static light is in the lightmaps - VOBs and NPCs only.
            lightComp.IsBakedIntoWorld = vob.LightStatic && _stationaryLightsService.HasWorldLightmaps;

            // DeveloperConfig.EnablePooledStationaryLights: fire lights share the pool too (closest to the camera).
            if (isRuntimeSlot || _configService.Dev.EnablePooledStationaryLights)
            {
                lightComp.IsRuntimeSlot = true;
                lightComp.RuntimeLinearColor = lightComp.Color.linear;
                lightComp.Init();
                return go;
            }

            // SaveGames might contain different order of Light objects (or Vobs where lights are children).
            // We therefore need to fetch which Vob has the same position as the one from cache.
            // Hint: The StationaryLights array should be ~512 elements max. If loading is slow,
            // we could also simply remove elements which are set to LightGOs already.
            lightComp.Index = _staticCacheService.LoadedStationaryLights.StationaryLights.FirstIndex(i =>
                i.P == worldPosition);

            if (lightComp.Index == -1)
            {
                Logger.LogWarning($"Light {vob.Name} not found in StaticCache. Therefore no LightIndex set and ignored by shader. " +
                                  $"Will be solved later. ;-)", LogCat.Vob);
            }

            lightComp.Init();

            return go;
        }

        private GameObject CreateMover(IMover vob, GameObject parent)
        {
            vob.MoverState = (int)VmGothicEnums.MoverState.Closed;

            var go = CreateDefaultMesh(vob, parent);

            if (go == null)
            {
                go = new GameObject($"Mover_{vob.Name}");
                go.transform.SetParent(parent != null ? parent.transform : null);
                go.transform.SetPositionAndRotation(
                    vob.Position.ToUnityVector(),
                    vob.Rotation.ToUnityQuaternion());
            }

            var adapter = go.AddComponent<MoverAdapter>();
            adapter.Init(vob, vob.Target);

            return go;
        }

        private GameObject CreateItem(IItem vob, GameObject parent)
        {
            string itemName;

            if (!string.IsNullOrEmpty(vob.Instance))
                itemName = vob.Instance;
            else if (!string.IsNullOrEmpty(vob.Name))
                itemName = vob.Name;
            else
                throw new Exception("Vob Item -> no usable name found.");

            var item = _vmCacheService.TryGetItemData(itemName);

            if (item == null)
                return null;

            var prefabInstance = GetPrefab(vob);
            var vobObj = CreateItemMesh(item, prefabInstance, parent);

            if (vobObj == null)
            {
                Object.Destroy(prefabInstance); // No mesh created. Delete the prefab instance again.
                Logger.LogWarning(
                    $"There should be no! object which can't be found n:{vob.Name} i:{vob.Instance}. " +
                    $"We need to use >PxVobItem.instance< to do it right!", LogCat.Vob);
                return null;
            }

            return vobObj;
        }

        private GameObject CreateItemMesh(ItemInstance item, GameObject go, GameObject parent)
        {
            var mrm = _resourceCacheService.TryGetMultiResolutionMesh(item.Visual);

            if (mrm != null)
            {
                return _meshService.CreateVob(item.Visual, mrm, parent: parent, rootGo: go, useColliderCache: true, useTextureArray: false);
            }

            // shortbow (itrw_bow_l_01) has no mrm, but has mmb
            var mmb = _resourceCacheService.TryGetMorphMesh(item.Visual);

            return _meshService.CreateVob(item.Visual, mmb, parent: parent, rootGo: go, useColliderCache: true);
        }

        public GameObject CreateItemMesh(ItemInstance item, GameObject parentGo, Vector3 position)
        {
            var mrm = _resourceCacheService.TryGetMultiResolutionMesh(item.Visual);
            if (mrm != null)
            {
                return _meshService.CreateVob(item.Visual, mrm, position, default, false, parentGo, useTextureArray: false);
            }

            // shortbow (itrw_bow_l_01) has no mrm, but has mmb
            var mmb = _resourceCacheService.TryGetMorphMesh(item.Visual);

            return _meshService.CreateVob(item.Visual, mmb, position, default, parentGo, useColliderCache: true);
        }


        [CanBeNull]
        private GameObject CreateMobContainer(Container vob, GameObject parent = null)
        {
            var vobObj = CreateDefaultMesh(vob, parent);

            if (vobObj == null)
            {
                Logger.LogWarning($"{vob.Name} - mesh for MobContainer not found.", LogCat.Vob);
                return null;
            }

            return vobObj;
        }

        [CanBeNull]
        private GameObject CreateSound(Sound vob, GameObject parent)
        {
            var go = GetPrefab(vob, parent);
            go.name = $"{vob.SoundName}";

            // This value is always true when a new game/world is loaded. (Compared with G1 save game.)
            vob.ShowVisual = false;
            vob.IsAllowedToRun = true;

            var source = go.GetComponent<AudioSource>();

            go.GetComponent<SoundHandler>().Init(vob);
            PrepareAudioSource(source, vob, vob.SoundName);
            
            _vobSoundCullingService.AddCullingEntry(go, vob);

            return go;
        }

        /// <summary>
        /// Creating AudioSource from PxVobSoundDaytimeData is very similar to PxVobSoundData one.
        /// There are only two differences:
        ///     1. This one has two AudioSources
        ///     2. The sources will be toggled during gameplay when start/end time is reached.
        /// </summary>
        [CanBeNull]
        private GameObject CreateSoundDaytime(SoundDaytime vob, GameObject parent)
        {
            var go = GetPrefab(vob, parent);
            go.name = $"{vob.SoundName}-{vob.SoundNameDaytime}";
            
            var sources = go.GetComponents<AudioSource>();

            PrepareAudioSource(sources[0], vob, vob.SoundName);
            PrepareAudioSource(sources[1], vob, vob.SoundNameDaytime);

            _vobSoundCullingService.AddCullingEntry(go, vob);

            return go;
        }

        private void PrepareAudioSource(AudioSource source, Sound soundData, string soundName)
        {
            source.maxDistance = soundData.Radius / 100f; // Gothic's values are in cm, Unity's in m.
            source.volume = soundData.Volume / 100f; // Gothic's volume is 0...100, Unity's is 0...1.

            // Random sounds shouldn't play initially, but after certain time.
            source.playOnAwake = soundData.InitiallyPlaying && soundData.Mode != SoundMode.Random;
            source.loop = soundData.Mode == SoundMode.Loop;
            source.spatialBlend = soundData.Ambient3d ? 1f : 0f;
            
            source.clip = GetSoundClip(soundName);
        }

        public AudioClip GetSoundClip(string soundName)
        {
            AudioClip clip;

            if (soundName.EqualsIgnoreCase(AudioService.NoSoundName))
            {
                //instead of decoding nosound.wav which might be decoded incorrectly, just return null
                return null;
            }

            // Bugfix - Normally the data is to get C_SFX_DEF entries from VM. But sometimes there might be the real .wav file stored.
            if (soundName.EndsWithIgnoreCase(".wav"))
            {
                clip = _audioService.CreateAudioClip(soundName);
            }
            else
            {
                var sfxContainer = _vmCacheService.TryGetSfxData(soundName);

                if (sfxContainer == null)
                    return null;

                var firstSound = sfxContainer.GetFirstSound();
                if (firstSound == null)
                    return null;

                // Instead of decoding nosound.wav which might be decoded incorrectly, just return null.
                if (firstSound.File.EqualsIgnoreCase(AudioService.NoSoundName))
                    return null;

                if (sfxContainer.Count > 1)
                    Logger.LogWarning($"Multiple random elements exist for >{firstSound.File}< but only first is selected.", LogCat.Audio);

                clip = _audioService.CreateAudioClip(firstSound.File);
            }

            return clip;
        }

        private GameObject CreateZoneMusic(ZoneMusic vob, GameObject parent)
        {
            var go = GetPrefab(vob);
            go.SetParent(parent, true, true);
            go.name = vob.Name;

            go.layer = Constants.IgnoreRaycastLayer;

            var min = vob.BoundingBox.Min.ToUnityVector();
            var max = vob.BoundingBox.Max.ToUnityVector();

            go.transform.position = (min + max) / 2f;
            go.transform.localScale = max - min;

            return go;
        }

        /// <summary>
        /// Basically a free point where NPCs can do something like sitting on a bench etc.
        /// @see for more information: https://ataulien.github.io/Inside-Gothic/objects/spot/
        /// </summary>
        private GameObject CreateSpot(IVirtualObject vob, GameObject parent, Vector3 worldPosition, bool debugDraw = false)
        {
            // FIXME - change to a Prefab in the future.
            var vobObj = GetPrefab(vob);

            if (debugDraw)
            {
                var rend = vobObj.AddComponent<MeshRenderer>();
                var filter = vobObj.AddComponent<MeshFilter>();
                filter.sharedMesh = Resources.GetBuiltinResource<Mesh>("Sphere.fbx");
                rend.sharedMaterial = Constants.DebugMaterial;
            }

            var fpName = vob.Name.IsEmpty() ? "START" : vob.Name;
            vobObj.name = fpName;
            vobObj.SetParent(parent, true, true);

            var freePointData = new FreePoint
            {
                Name = fpName,
                // worldPosition is the accumulated parent chain offset + vob.Position — use it
                // instead of the raw local vob.Position so nested FPs get the correct world coords.
                Position = worldPosition,
                Direction = vob.Rotation.ToUnityQuaternion().eulerAngles
            };
            vobObj.GetComponent<VobSpotProperties>().Fp = freePointData;
            _gameStateService.FreePoints.TryAdd(fpName, freePointData);

            return vobObj;
        }

        private GameObject CreateTriggerChangeLevel(TriggerChangeLevel vob, GameObject parent)
        {
            var vobObj = GetPrefab(vob, parent);

            var min = vob.BoundingBox.Min.ToUnityVector();
            var max = vob.BoundingBox.Max.ToUnityVector();
            vobObj.transform.position = (min + max) / 2f;

            vobObj.transform.localScale = max - min;

            var triggerHandler = vobObj.GetComponent<ChangeLevelTriggerHandler>();
            triggerHandler.LevelName = vob.LevelName;
            triggerHandler.StartVob = vob.StartVob;
            return vobObj;
        }

        private GameObject CreateTriggerScript(ITriggerScript vob, GameObject parent)
        {
            var vobObj = GetPrefab(vob, parent);

            var min = vob.BoundingBox.Min.ToUnityVector();
            var max = vob.BoundingBox.Max.ToUnityVector();
            vobObj.transform.position = (min + max) / 2f;

            vobObj.transform.localScale = max - min;

            vobObj.GetComponent<TriggerScriptHandler>().Init(vob);

            return vobObj;
        }

        private GameObject CreateTriggerList(ITriggerList vob, GameObject parent)
        {
            var go = new GameObject($"TriggerList_{vob.Name}");
            go.transform.SetParent(parent != null ? parent.transform : null);
            go.AddComponent<TriggerListHandler>().Init(vob);
            return go;
        }

        private GameObject CreateCodeMaster(ICodeMaster vob, GameObject parent)
        {
            var go = new GameObject($"CodeMaster_{vob.Name}");
            go.transform.SetParent(parent != null ? parent.transform : null);
            go.AddComponent<CodeMasterHandler>().Init(vob);
            return go;
        }

        private GameObject CreateTriggerZone(ITrigger vob, GameObject parent)
        {
            var go = new GameObject($"TriggerZone_{vob.Name}");
            go.transform.SetParent(parent != null ? parent.transform : null);

            var min = vob.BoundingBox.Min.ToUnityVector();
            var max = vob.BoundingBox.Max.ToUnityVector();
            go.transform.position = (min + max) / 2f;
            go.transform.localScale = Vector3.Max(max - min, Vector3.one * 0.01f);

            var col = go.AddComponent<BoxCollider>();
            col.isTrigger = true;

            go.AddComponent<TriggerZoneHandler>().Init(vob);
            return go;
        }

        private GameObject CreateTriggerUntouch(ITriggerUntouch vob, GameObject parent)
        {
            var go = new GameObject($"TriggerUntouch_{vob.Name}");
            go.transform.SetParent(parent != null ? parent.transform : null);

            var min = vob.BoundingBox.Min.ToUnityVector();
            var max = vob.BoundingBox.Max.ToUnityVector();
            go.transform.position = (min + max) / 2f;
            go.transform.localScale = Vector3.Max(max - min, Vector3.one * 0.01f);

            var col = go.AddComponent<BoxCollider>();
            col.isTrigger = true;

            go.AddComponent<TriggerUntouchHandler>().Init(vob);
            return go;
        }

        private GameObject CreateTriggerWorldStart(ITriggerWorldStart vob, GameObject parent)
        {
            var go = new GameObject($"TriggerWorldStart_{vob.Name}");
            go.transform.SetParent(parent != null ? parent.transform : null);
            go.AddComponent<TriggerWorldStartHandler>().Init(vob);
            return go;
        }

        private GameObject CreateMoverController(IMoverController vob, GameObject parent)
        {
            var go = new GameObject($"MoverController_{vob.Name}");
            go.transform.SetParent(parent != null ? parent.transform : null);
            go.AddComponent<MoverControllerHandler>().Init(vob);
            return go;
        }

        private GameObject CreateMessageFilter(IMessageFilter vob, GameObject parent)
        {
            var go = new GameObject($"MessageFilter_{vob.Name}");
            go.transform.SetParent(parent != null ? parent.transform : null);
            go.AddComponent<MessageFilterHandler>().Init(vob);
            return go;
        }

        private GameObject CreateAnimatedVob(Animate vob, GameObject parent = null)
        {
            var go = CreateDefaultMesh(vob, parent);
            var morph = go.GetComponent<VobAnimateMorph>();
            morph.StartAnimation(vob.Visual!.Name);
            return go;
        }

        /// <summary>
        /// DeveloperConfig.EnableDigSpots: G2's treasure X marks (TREASURE_ADDON_01.ASC) are a single attachment mesh with
        /// an untextured, color-only material. MeshService skips texture-less models (on purpose for G1's empty ones like
        /// the harp or WASH_SLOT), so the X - and with it its VRFocus/VRDigSpot - got destroyed: invisible and not
        /// diggable. Mobs used with a tool that have an onStateFunc are kept with a simple red X instead.
        /// </summary>
        private bool TryKeepAsDigSpotPlaceholder(IVirtualObject vob, GameObject go, GameObject parent)
        {
            if (!_configService.Dev.EnableDigSpots || go == null)
                return false;
            if (vob is not IInteractiveObject mob || vob is IContainer || vob is IDoor ||
                mob.Item.IsNullOrEmpty() || mob.OnStateChangeFunction.IsNullOrEmpty())
                return false;

            go.name = vob.GetVisualName();
            if (parent != null)
                go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;

            // In G2 the X is a child of the RAKEPLACE decal, which (WIP EnableDecalVisuals) applies the vob's
            // position+rotation a second time below its already placed loader. Snap to the loader, upright.
            var loader = go.GetComponentInParent<VobLoader>();
            if (loader != null)
                go.transform.SetPositionAndRotation(loader.transform.position,
                    Quaternion.Euler(0f, loader.transform.eulerAngles.y, 0f));

            // Child vobs have no VobLoader/VobContainer of their own - GetComponentInParent<VobLoader>() found the
            // decal's loader, so VRFocus/VRDigSpot saw a zCVob instead of this mob. Give the X its own one.
            if (loader == null || loader.Container?.Vob != vob)
            {
                var ownLoader = go.AddComponent<VobLoader>();
                ownLoader.Container = new VobContainer(vob) { Go = go };
                ownLoader.IsLoaded = true;
            }

            // Two crossed red planks, roughly the size of the original X.
            CreateDigSpotPlank(go, 45f);
            CreateDigSpotPlank(go, -45f);

            Logger.Log($"[DigSpot] '{vob.Name}' has no textured mesh ({vob.GetVisualName()}) - using a placeholder X.", LogCat.Vob);
            return true;
        }

        /// <summary>
        /// DeveloperConfig.EnableDamageTargetMarkers: a trigger/mover you shoot (reactToOnDamage) with nothing visible -
        /// its mesh didn't load or it has none (G2: the targets for bows/spells that open movers weren't there in builds).
        /// A green X in the middle of its zone shows where to shoot, like the red X of treasure spots.
        /// </summary>
        private void TryAddDamageTargetMarker(IVirtualObject vob, GameObject go)
        {
            if (!_configService.Dev.EnableDamageTargetMarkers || go == null || vob is not ITrigger { ReactToOnDamage: true })
                return;
            foreach (var existingRenderer in go.GetComponentsInChildren<Renderer>(true))
            {
                if (existingRenderer.enabled)
                    return;
            }

            // Not as a child right away: trigger zones are scaled to their bounding box - the X keeps its own size.
            var marker = new GameObject("DamageTargetMarker");
            var min = vob.BoundingBox.Min.ToUnityVector();
            var max = vob.BoundingBox.Max.ToUnityVector();
            var hasBounds = (max - min).sqrMagnitude > 0.0001f;
            marker.transform.SetPositionAndRotation(hasBounds ? (min + max) / 2f : go.transform.position,
                Quaternion.Euler(0f, vob.Rotation.ToUnityQuaternion().eulerAngles.y, 0f));
            marker.transform.SetParent(go.transform, true);

            var green = new Color(0.1f, 0.75f, 0.15f);
            CreateMarkerPlank(marker, Quaternion.Euler(0f, 0f, 45f), green);
            CreateMarkerPlank(marker, Quaternion.Euler(0f, 0f, -45f), green);

            Logger.Log($"[DamageTarget] '{vob.Name}' ({vob.Type}) reacts to hits but shows nothing - green X at " +
                       $"{marker.transform.position}", LogCat.Vob);
        }

        private static void CreateMarkerPlank(GameObject parent, Quaternion localRotation, Color color)
        {
            var plank = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plank.name = "DamageTargetMarkerPlank";
            plank.transform.SetParent(parent.transform, false);
            plank.transform.localRotation = localRotation;
            plank.transform.localScale = new Vector3(0.6f, 0.08f, 0.08f);

            // Only visual - the trigger's own zone/collider takes the hit.
            Object.Destroy(plank.GetComponent<Collider>());

            var material = GetPlaceholderMaterial(color);
            if (material != null)
                plank.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static readonly Dictionary<Color, Material> _placeholderMaterials = new();

        /// <summary>
        /// The placeholder X's material: URP Unlit in a plain color. The primitive's default material drew nothing in
        /// builds - G2's treasure X marks (and the green target X) were there and diggable, but invisible.
        /// </summary>
        private static Material GetPlaceholderMaterial(Color color)
        {
            if (_placeholderMaterials.TryGetValue(color, out var material) && material != null)
                return material;

            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                Logger.LogWarning("[DigSpot] URP Unlit shader not found - placeholder X keeps the default material.", LogCat.Vob);
                return null;
            }

            material = new Material(shader) { color = color };
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            _placeholderMaterials[color] = material;
            return material;
        }

        private static void CreateDigSpotPlank(GameObject parent, float yaw)
        {
            var plank = GameObject.CreatePrimitive(PrimitiveType.Cube);
            plank.name = "DigSpotPlaceholder";
            plank.transform.SetParent(parent.transform, false);
            plank.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            plank.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            plank.transform.localScale = new Vector3(0.9f, 0.05f, 0.12f);

            // Only visual - VRDigSpot compares bounds, no physics needed.
            Object.Destroy(plank.GetComponent<Collider>());

            var material = GetPlaceholderMaterial(new Color(0.6f, 0.05f, 0.05f));
            if (material != null)
                plank.GetComponent<Renderer>().sharedMaterial = material;
        }

        private GameObject CreateDefaultMesh(IVirtualObject vob, GameObject parent)
        {
            var go = GetPrefab(vob);
            var meshName = vob.GetVisualName();

            if (meshName.IsNullOrEmpty())
            {
                Logger.LogWarning("VisualName is empty.", LogCat.Vob);
                return null;
            }

            // MDL
            var mdl = _resourceCacheService.TryGetModel(meshName, false);
            if (mdl != null)
            {
                var ret = _meshService.CreateVob(meshName, mdl, parent: parent, rootGo: go);

                // A few objects are broken and have no meshes. We need to destroy them immediately again.
                if (ret == null)
                {
                    if (TryKeepAsDigSpotPlaceholder(vob, go, parent))
                        return go;

                    Object.Destroy(go);
                }

                return ret;
            }

            // MDH+MDM (without MDL as wrapper)
            var mdh = _resourceCacheService.TryGetModelHierarchy(meshName, false);
            var mdm = _resourceCacheService.TryGetModelMesh(meshName, false);
            if (mdh != null && mdm != null)
            {
                var ret = _meshService.CreateVob(meshName, mdm, mdh, parent: parent, rootGo: go);

                // A few objects are broken and have no meshes. We need to destroy them immediately again.
                if (ret == null)
                {
                    Object.Destroy(go);
                }

                return ret;
            }

            // MMB
            var mmb = _resourceCacheService.TryGetMorphMesh(meshName, false);
            if (mmb != null)
            {
                var ret = _meshService.CreateVob(meshName, mmb, parent: parent, rootGo: go, useTextureArray: true);

                // A few objects are broken and have no meshes. We need to destroy them immediately again.
                if (ret == null)
                {
                    Object.Destroy(go);
                }

                return ret;
            }

            // MRM
            var mrm = _resourceCacheService.TryGetMultiResolutionMesh(meshName, false);
            if (mrm != null)
            {
                // If the object is a dynamic one, it will collide.
                var withCollider = vob.CdDynamic;

                var ret = _meshService.CreateVob(meshName, mrm, withCollider: withCollider, parent: parent, rootGo: go);

                // A few objects are broken and have no meshes. We need to destroy them immediately again.
                if (ret == null)
                {
                    Object.Destroy(go);
                }

                return ret;
            }

            Logger.LogWarning($">{meshName}<'s has no mdl/mdh+mdm/mmb/mrm.", LogCat.Vob);
            return null;
        }

        private GameObject CreateDecal(IVirtualObject vob, GameObject parent)
        {
            return _meshService.CreateVobDecal(vob, (VisualDecal)vob.Visual,
                vob.Position.ToUnityVector(), vob.Rotation.ToUnityQuaternion(), parent);
        }

        /// <summary>
        /// DeveloperConfig.EnablePfxControllerVisuals: the controller's effect is its particle visual like the engine
        /// (zCPFXControler, OpenGothic PfxController) - pfxName is mostly a Spacer leftover ".PFX". Before, G2's active
        /// teleport platforms (RESURRECTION.pfx) and cauldrons showed nothing ("PFX '.PFX' not found in VM").
        /// </summary>
        private string GetPfxControllerEffect(IParticleEffectController controller)
        {
            if (_configService.Dev.EnablePfxControllerVisuals && controller.Visual?.Type == VisualType.ParticleEffect &&
                controller.Visual.Name.NotNullOrEmpty())
                return controller.Visual.Name;
            return controller.EffectName;
        }

        private GameObject CreatePfx(IVirtualObject vob, GameObject parent, bool isInRelativeTree = false)
        {
            return _meshService.CreateVobPfx(vob, GetPfxPosition(vob, parent, isInRelativeTree),
                vob.Rotation.ToUnityQuaternion(), parent);
        }

        /// <summary>
        /// Particle effects are placed in world space. DeveloperConfig.EnableFireTreeRelativePositions: in a fire tree
        /// (oCMobFire vobTree) the VOB position is relative to the fire's slot - taken as world position, every fire,
        /// torch flame and smoke of the world burned at the world's origin (torches showed no fire at all).
        /// </summary>
        private Vector3 GetPfxPosition(IVirtualObject vob, GameObject parent, bool isInRelativeTree)
        {
            var position = vob.Position.ToUnityVector();
            if (isInRelativeTree && parent != null && _configService.Dev.EnableFireTreeRelativePositions)
                position += parent.transform.position;
            return position;
        }

        /// <summary>
        /// Some objects are kind of null. We have Position only. this method is to compare with Gothic Spacer and remove if not needed later.
        /// </summary>
        private GameObject CreateDebugObject(IVirtualObject vob, GameObject parent = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = $"{vob.Name} - Empty DEBUG object. Check with Spacer if buggy.";
            return go;
        }
    }
}
