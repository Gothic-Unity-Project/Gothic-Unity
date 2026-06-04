using System;
using System.Collections.Generic;
using Gothic.Core.Adapters.Properties;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Const;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Extensions;
using Gothic.Core.Models.Caches;
using Gothic.Core.Models.Vm;
using Gothic.Core.Models.Vob.WayNet;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Culling;
using Gothic.Core.Services.Meshes;
using Gothic.Core.Services.StaticCache;
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


        /// <summary>
        /// Generic root method to create a VOB of any type.
        /// Type specific creation will be handled inside further Create*() methods.
        ///
        /// parentWorldPosition - When we load Lights, we need to assign the shader's Index. As lazy loading will randomly load them,
        ///                       and Lights could be sub-VOBs inside fire, we need to find the index from WorldPosition calculated at
        ///                       caching times. Same calculation! (ZenKit.IVirtualVob.Position + ZK.parentPos
        /// </summary>
        public void InitVob(IVirtualObject vob, GameObject parent, Vector3 parentWorldPosition, bool isRootVob)
        {
            var worldPosition = parentWorldPosition + vob.Position.ToUnityVector();
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
                    go = CreateSpot(vob, parent, _configService.Dev.ShowFreePoints);
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
                                go = CreatePfx(vob, parent);
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
                    break;
                case VirtualObjectType.zCPFXController:
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

                    // For SaveGame comparison, we load our fallback Prefab and set VobProperties.
                    // Remove it from here once we properly implement and handle it.
                    CreateEmptyDefaultVob(vob, parent);
                    return;
                case VirtualObjectType.oCTriggerScript:
                    go = CreateTriggerScript((ITriggerScript)vob, parent);
                    return;
                case VirtualObjectType.zCVobScreenFX:
                case VirtualObjectType.zCTriggerWorldStart:
                case VirtualObjectType.oCCSTrigger:
                case VirtualObjectType.zCVobLensFlare:
                case VirtualObjectType.zCMoverController:
                case VirtualObjectType.zCZoneZFog:
                case VirtualObjectType.zCZoneZFogDefault:
                case VirtualObjectType.zCZoneVobFarPlane:
                case VirtualObjectType.zCZoneVobFarPlaneDefault:
                case VirtualObjectType.zCMessageFilter:
                case VirtualObjectType.zCCodeMaster:
                case VirtualObjectType.zCCSCamera:
                case VirtualObjectType.zCCamTrj_KeyFrame:
                case VirtualObjectType.oCTouchDamage:
                case VirtualObjectType.zCTriggerUntouch:
                case VirtualObjectType.zCEarthquake:
                case VirtualObjectType.zCTrigger:
                case VirtualObjectType.Ignored:
                case VirtualObjectType.Unknown:
                    // For SaveGame comparison, we load our fallback Prefab and set VobProperties.
                    // Remove it from here once we properly implement and handle it.
                    CreateEmptyDefaultVob(vob, parent);
                    return;
                case VirtualObjectType.zCVobLevelCompo:
                    // Nothing to do.
                    return;
                default:
                    Logger.LogError($"VobType={vob.Type} not yet handled. And we didn't know we need to do so. ;-)", LogCat.Vob);
                    return;
            }

            // Do not check children if the current VOB can't be created.
            if (!go)
                return;
            
            foreach (var childVob in vob.Children)
            {
                InitVob(childVob, go, worldPosition, false);
            }
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
                    else if (name.EqualsIgnoreCase("ItKeLockpick"))
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
                    go = _resourceCacheService.TryGetPrefabObject(PrefabType.VobDoor, name: name, parent: parent);
                    break;
                case VirtualObjectType.oCMobContainer:
                    go = _resourceCacheService.TryGetPrefabObject(PrefabType.VobContainer, name: name, parent: parent);
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

            if (vob.VobTree == "")
            {
                return null;
            }

            var fireWorld = _resourceCacheService.TryGetWorld(vob.VobTree, _contextGameVersionService.Version, true);

            if (fireWorld == null)
            {
                Logger.LogWarning($"Fire world {vob.VobTree} not found. Skipping usage...", LogCat.Loading);
                return null;
            }
            
            CreateFireVobs(fireWorld.RootObjects, go.FindChildRecursively(vob.Slot) ?? go, worldPosition);

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
                InitVob(vob, parent, worldPosition, false);
            }
        }

        private GameObject CreateLight(Light vob, GameObject parent, Vector3 worldPosition)
        {
            if (vob.LightStatic)
            {
                // Logging these will cause stuttering when a lot of them are needed.
                // Logger.LogWarning($"Non-static lights aren't handled so far. go={vob.Name}");
                return null;
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
            // Each mover starts "Closed", when game boots. (At least for a new game.)
            // TODO - We need to check if it's the case for a loaded game
            vob.MoverState = (int)VmGothicEnums.MoverState.Closed;

            // TODO - We need to implement animations for this vob type
            var go = CreateDefaultMesh(vob, parent);

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
                return _meshService.CreateVob(item.Visual, mrm, parent: parent, rootGo: go, useColliderCache: true);
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

                // Instead of decoding nosound.wav which might be decoded incorrectly, just return null.
                if (sfxContainer.GetFirstSound().File.EqualsIgnoreCase(AudioService.NoSoundName))
                    return null;

                if (sfxContainer.Count > 1)
                    Logger.LogWarning($"Multiple random elements exist for >{sfxContainer.GetFirstSound().File}< but only first is selected.", LogCat.Audio);

                clip = _audioService.CreateAudioClip(sfxContainer.GetFirstSound().File);
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
        private GameObject CreateSpot(IVirtualObject vob, GameObject parent, bool debugDraw = false)
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
                Position = vob.Position.ToUnityVector(),
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

        private GameObject CreateAnimatedVob(Animate vob, GameObject parent = null)
        {
            var go = CreateDefaultMesh(vob, parent);
            var morph = go.GetComponent<VobAnimateMorph>();
            morph.StartAnimation(vob.Visual!.Name);
            return go;
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

        private GameObject CreatePfx(IVirtualObject vob, GameObject parent)
        {
            return _meshService.CreateVobPfx(vob, vob.Position.ToUnityVector(), vob.Rotation.ToUnityQuaternion(), parent);
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
