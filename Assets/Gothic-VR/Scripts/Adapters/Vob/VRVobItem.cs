#if GOTHIC_HVR_INSTALLED
using System;
using System.Collections;
using Gothic.Core.Adapters.Properties.Vobs;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Const;
using Gothic.Core.Manager;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Culling;
using Gothic.Core.Services.Meshes;
using Gothic.VR.Services;
using Gothic.Core;
using Gothic.Core.Logging;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using HurricaneVR.Framework.Core;
using HurricaneVR.Framework.Core.Grabbers;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.Animations;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Vob
{
    public class VRVobItem : MonoBehaviour
    {
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly MarvinService _marvinService;
        [Inject] private readonly DynamicMaterialService _dynamicMaterialService;
        [Inject] private readonly VobMeshCullingService _vobMeshCullingService;
        [Inject] private readonly DocService _docService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;
        [Inject] private readonly Gothic.Core.Services.Vobs.VobService _vobService;
        [Inject] private readonly Gothic.Core.Services.Player.PlayerService _playerService;

        // DeveloperConfig.EnableDocConsumeOnClose: a sealed letter (MAPSEALED) is used up when it's read - but only
        // when the reading ends (one hand lets go), not while it's still in front of the player.
        private bool _isConsumedOnDocClose;

        [SerializeField] private VRVobItemProperties _vrProperties;
        [SerializeField] private Rigidbody _rigidbody;
        [SerializeField] private MeshCollider _meshCollider;

        // We pre-allocate enough entries to fetch at least one entry which is not ourselves.
        private readonly Collider[] _overlapColliders = new Collider[1];
        private static int _ignoreLayerCollisionCheck;


        private struct OverlapCheckData
        {
            public Axis MaxAxis; // Used for P0 and P1 calculation
            public  Axis SecondMaxAxis; // Used for radius calculation
            public  Vector3 Center;
            public  Vector3 OverlapPoint0; // First point where the sphere will start to draw its closing circle
            public  Vector3 OverlapPoint1; // Second point with the closing circle calculation. Will be opposite of first one based on mainAxis
            public  float OverlapRadius; // Radius is calculated as /2 calculation from size of secondMaxAxis
        }


        private void Awake()
        {
            // Pre-fill calculation data before being used every frame.
            if (_ignoreLayerCollisionCheck == 0)
            {
                _ignoreLayerCollisionCheck = Constants.VobItemNoWorldCollision | Constants.HandLayer;
            }
        }

        private void Start()
        {
            GetComponent<HVRGrabbable>().SetupColliders();
        }

        /// <summary>
        /// 1. Grabbed via ForceGrabber (remote grabbing into hand)
        /// 2. Grabbed via HandGrabber (direct grabbing into hand)
        /// 3. Grabbed via Socket (e.g., backpack)
        /// </summary>
        public void OnGrabbed(HVRGrabberBase grabber, HVRGrabbable grabbable)
        {
            // Ignore grabbing once, if MarvinSelectionMode is active.
            if (_marvinService.IsMarvinSelectionMode)
            {
                _marvinService.MarvinSelectionGO = grabbable.gameObject;
                return;
            }

            // OnGrabbed is normally called multiple times. Even after an object is already socketed. If so, then let's stop Grab behaviour.
            // If we sock an object on our hips or backpack etc.
            if (grabber is HVRSocket)
                return;

            // Stop collisions while being dragged around (at least shortly; otherwise e.g. items might stick inside chests when pulled out).
            gameObject.layer = Constants.VobItemNoWorldCollision;

            // At least until object isn't colliding with anything any longer, the object will be a ghost (i.e. no collision + transparency activated)
            _dynamicMaterialService.SetDynamicValue(gameObject, Constants.ShaderPropertyTransparency, Constants.ShaderPropertyTransparencyValue);

            // If we want Item collisions, we just temporarily deactivate them until the item is free of collisions.
            StartCoroutine(ReEnableCollisionRoutine());
            
            _vobMeshCullingService?.StartTrackVobPositionUpdates(gameObject);
            _vrPlayerService.SetGrab(grabber, grabbable);

            // One hand is enough for ranged weapons and torches.
            TryPrepareRangedWeapon();
            TryPrepareTorch();

            if (_vrPlayerService.IsDualGrabbed)
            {
                TryShowDocument();
                TryCastSpell();
                TryPrepareItemUse();
            }
        }

        /// <summary>
        /// 1. Grabbed via ForceGrabber (remote grabbing into hand)
        /// 2. Grabbed via HandGrabber (direct grabbing into hand)
        /// 3. Grabbed via Socket (e.g., backpack)
        /// </summary>
        public void OnReleased(HVRGrabberBase grabber, HVRGrabbable grabbable)
        {
            // If we release an object from our hips or backpack etc.
            // We then assume, that the item is already with correct values as it was grabbed before (shader, layer, ...)
            if (grabber is HVRSocket)
                return;

            gameObject.layer = Constants.VobItemLayer; // Back to default

            // Disable "ghostification" of object.
            _dynamicMaterialService.ResetDynamicValue(gameObject, Constants.ShaderPropertyTransparency, Constants.ShaderPropertyTransparencyDefault);

            _vobMeshCullingService?.StopTrackVobPositionUpdates(gameObject);
            _vrPlayerService.UnsetGrab(grabber, grabbable);

            // Bows/crossbows shoot as long as any hand holds them. Removed immediately: a release + grab in the same
            // frame (holster -> hand, hand -> hand) found the component still pending destruction and added no new
            // one - the bow ended up without its string.
            var isStillHeld = _vrPlayerService.GrabbedItemLeft == gameObject || _vrPlayerService.GrabbedItemRight == gameObject;
            if (!isStillHeld && TryGetComponent<Adapters.Vob.VobItem.VRCrossbow>(out var crossbow))
                DestroyImmediate(crossbow);
            if (!isStillHeld && TryGetComponent<Adapters.Vob.VobItem.VRBow>(out var bow))
                DestroyImmediate(bow);

            // Close any open document viewer / rune caster when item is no longer dual-grabbed.
            if (!_vrPlayerService.IsDualGrabbed)
            {
                var viewer = GetComponent<Adapters.Vob.VobItem.VRDocViewer>();
                if (viewer != null)
                    Destroy(viewer);
                foreach (Transform child in transform)
                {
                    if (child.name == "_DocCanvas")
                        Destroy(child.gameObject);
                }

                if (_isConsumedOnDocClose)
                {
                    _isConsumedOnDocClose = false;
                    ConsumeDocument();
                    return;
                }

                var itemUser = GetComponent<Adapters.Vob.VobItem.VRItemUser>();
                if (itemUser != null)
                    Destroy(itemUser);

                var caster = GetComponent<Adapters.Vob.VobItem.VRRuneCaster>();
                if (caster != null)
                {
                    // During telekinesis, keep the caster alive while one hand still holds the rune —
                    // the freed hand needs to grab the distant item. Destroy when both hands are clear.
                    // Throwable spells (DeveloperConfig.EnableThrowableSpells) are held in one hand by design.
                    var runeStillHeld = _vrPlayerService.GrabbedItemLeft == gameObject
                                     || _vrPlayerService.GrabbedItemRight == gameObject;
                    var keepAlive = runeStillHeld
                        && (_vrPlayerService.IsTelekinesisActive || caster.IsTargetingActive || caster.IsThrowable);
                    if (!keepAlive)
                        Destroy(caster);
                }
            }
        }

        private void TryShowDocument()
        {
            var item = GetComponentInParent<VobLoader>()?.Container.PropsAs<VobItemProperties2>()?.Instance;
            if (item == null)
                return;

            var mainFlag = (VmGothicEnums.ItemFlags)item.MainFlag;
            if (mainFlag != VmGothicEnums.ItemFlags.ItemKatDocs)
                return;

            var onStateIndex = item.GetOnState(0);
            if (onStateIndex == 0)
                return;

            // Ensure VRDocViewer is present on this GO before Daedalus fires Doc_Show.
            if (GetComponent<Adapters.Vob.VobItem.VRDocViewer>() == null)
                gameObject.AddComponent<Adapters.Vob.VobItem.VRDocViewer>();

            var vm = _gameStateService.GothicVm;
            var oldSelf = vm.GlobalSelf;
            vm.GlobalSelf = vm.GlobalHero;
            _docService.PendingItemGo = gameObject;
            try
            {
                vm.Call(onStateIndex);
                Logger.Log($"[VRVobItem] Called on_state[0] for doc item {item.Name}", LogCat.VR);

                // G1 Usefireletter: CreateInvItem(ItWr_Fire_Letter_02) - the sealed one is used up by the engine.
                if (_configService.Dev.EnableDocConsumeOnClose &&
                    Adapters.Vob.VobItem.VRItemUser.IsConsumedOnUse(item, _resourceCacheService))
                    _isConsumedOnDocClose = true;
            }
            catch (Exception e)
            {
                Logger.LogError($"[VRVobItem] on_state[0] failed for {item.Name}: {e.Message}", LogCat.VR);
                _docService.PendingItemGo = null;
            }
            finally
            {
                vm.GlobalSelf = oldSelf;
            }
        }

        /// <summary>
        /// The read sealed document is used up: one of a stack, or the whole item (released from the other hand first,
        /// which also takes it out of the inventory like a dropped item).
        /// </summary>
        private void ConsumeDocument()
        {
            // Unpaid trader goods (trade counter) can be read, but not used up.
            if (GetComponentInParent<Gothic.VR.Adapters.Trade.VRTradeGoods>() is { IsSettled: false })
                return;

            var container = GetComponentInParent<VobLoader>()?.Container;
            if (container?.Vob is not ZenKit.Vobs.IItem vobItem)
                return;

            var instanceName = !string.IsNullOrEmpty(vobItem.Instance) ? vobItem.Instance : vobItem.Name;
            if (vobItem.Amount > 1)
            {
                vobItem.Amount--;
                if (GetComponent<HVRGrabbable>()?.IsBeingHeld == true)
                    _playerService.RemoveItem(instanceName, 1);
                Logger.Log($"[VRVobItem] {instanceName} read - one used up, remaining={vobItem.Amount}", LogCat.VR);
                return;
            }

            Logger.Log($"[VRVobItem] {instanceName} read - used up", LogCat.VR);
            var grabbable = GetComponent<HVRGrabbable>();
            if (grabbable != null && grabbable.IsBeingHeld)
                grabbable.ForceRelease();
            _vobService.RemoveWorldItem(container);
        }

        /// <summary>
        /// DeveloperConfig.EnableVrTorch: torches (ITEM_TORCH) get lit/put out with the trigger (VRTorch).
        /// </summary>
        private void TryPrepareTorch()
        {
            if (!_configService.Dev.EnableVrTorch || GetComponent<Adapters.Vob.VobItem.VRTorch>() != null)
                return;
            var item = GetComponentInParent<VobLoader>()?.Container.PropsAs<VobItemProperties2>()?.Instance;
            if (item != null && ((int)item.Flags & (int)VmGothicEnums.ItemFlags.ItemTorch) != 0)
                gameObject.AddComponent<Adapters.Vob.VobItem.VRTorch>();
        }

        private void TryCastSpell()
        {
            var item = GetComponentInParent<VobLoader>()?.Container.PropsAs<VobItemProperties2>()?.Instance;
            if (item == null)
                return;

            var mainFlag = (VmGothicEnums.ItemFlags)item.MainFlag;
            if (mainFlag != VmGothicEnums.ItemFlags.ItemKatRune)
                return;

            if (GetComponent<Adapters.Vob.VobItem.VRRuneCaster>() == null)
                gameObject.AddComponent<Adapters.Vob.VobItem.VRRuneCaster>();
        }

        /// <summary>
        /// V1 (DeveloperConfig.EnableVrCrossbow / EnableVrBows): ranged weapons shoot while held (vr-ranged-spells-plan.md).
        /// </summary>
        private void TryPrepareRangedWeapon()
        {
            if (GetComponent<Adapters.Vob.VobItem.VRCrossbow>() != null || GetComponent<Adapters.Vob.VobItem.VRBow>() != null)
                return;

            var item = GetComponentInParent<VobLoader>()?.Container.PropsAs<VobItemProperties2>()?.Instance;
            if (item == null || (VmGothicEnums.ItemFlags)item.MainFlag != VmGothicEnums.ItemFlags.ItemKatFf)
                return;

            var flags = (VmGothicEnums.ItemFlags)item.Flags;
            if (_configService.Dev.EnableVrCrossbow && flags.HasFlag(VmGothicEnums.ItemFlags.ItemCrossbow))
                gameObject.AddComponent<Adapters.Vob.VobItem.VRCrossbow>();
            else if (_configService.Dev.EnableVrBows && flags.HasFlag(VmGothicEnums.ItemFlags.ItemBow))
                gameObject.AddComponent<Adapters.Vob.VobItem.VRBow>();
        }

        /// <summary>
        /// Items with a "use" function (on_state[0]) like pouches. VRItemUser waits for the trigger while dual-grabbed.
        /// </summary>
        private void TryPrepareItemUse()
        {
            var item = GetComponentInParent<VobLoader>()?.Container.PropsAs<VobItemProperties2>()?.Instance;
            if (!Adapters.Vob.VobItem.VRItemUser.IsUsable(item, _configService.Dev.EnableEquipItems))
                return;

            if (GetComponent<Adapters.Vob.VobItem.VRItemUser>() == null)
                gameObject.AddComponent<Adapters.Vob.VobItem.VRItemUser>();
        }

        /// <summary>
        /// Draw Debug Gizmos for Physics.OverlapCapsule calculations and render them kind of visible. ;-)
        /// </summary>
        private void OnDrawGizmos()
        {
            if (!Application.isPlaying || !_configService.Dev.ShowCapsuleOverlapGizmos)
            {
                return;
            }

            var overlapData = CalculateOverlap();

            // The red line indicates the main axis and it's length.
            Gizmos.color = Color.red;
            Gizmos.DrawLine(overlapData.Center, overlapData.OverlapPoint1);

            // The green line indicates the second max axis (used for radius in OverlapCapsule check)
            Gizmos.color = Color.green;
            switch (overlapData.SecondMaxAxis)
            {
                case Axis.X:
                    Gizmos.DrawLine(overlapData.Center, overlapData.Center + new Vector3(overlapData.OverlapRadius, 0, 0));
                    break;
                case Axis.Y:
                    Gizmos.DrawLine(overlapData.Center, overlapData.Center + new Vector3(0, overlapData.OverlapRadius, 0));
                    break;
                default:
                    Gizmos.DrawLine(overlapData.Center, overlapData.Center + new Vector3(0, 0, overlapData.OverlapRadius));
                    break;
            }

            var mainMeshCollider = GetComponent<MeshCollider>();
            // Same calculation as in IsColliderOverlapping(). Used to print sphere's on the touch points for a visible check.
            var overlapColliders = Physics.OverlapCapsule(
                overlapData.OverlapPoint0,
                overlapData.OverlapPoint1,
                overlapData.OverlapRadius,
                Constants.VobItemNoWorldCollision | Constants.HandLayer);

            foreach (var overlapCollider in overlapColliders)
            {
                if (overlapCollider == mainMeshCollider)
                    continue;

                // Yellow indicates touch points between colliders. With this you can check if they collide at the right spot.
                Gizmos.color = Color.yellow;
                Gizmos.DrawSphere(overlapCollider.ClosestPointOnBounds(overlapData.Center), 0.05f);
            }
        }

        /// <summary>
        /// Check every n-th frame if the object has no collisions any longer. Then re-enable collisions.
        ///
        /// FIXME - If we want to have our sword always as a ghost, we need to properly implement it:
        ///         (1) a Setting in Immersion menu,
        ///         (2) properly set collision matrix as othwise hip holsters aren't detected because they're layer:default.
        /// </summary>
        private IEnumerator ReEnableCollisionRoutine()
        {
            // while (IsColliderOverlapping())
            yield return new WaitForSeconds(1f);

            // Re-enable collisions
            gameObject.layer = Constants.VobItemLayer;

            // Disable "ghostification" of object.
            _dynamicMaterialService.ResetDynamicValue(gameObject, Constants.ShaderPropertyTransparency, Constants.ShaderPropertyTransparencyDefault);
        }

        /// <summary>
        /// FIXME - Isn't working so far. It also collects ZoneMusic.
        ///         We need to properly design Layers to have the collider matrix work fine.
        /// Physics.OverlapCapsule() check if the item is free of collisions and therefore its collisions can be re-activated again.
        /// </summary>
        private bool IsColliderOverlapping()
        {
            var overlapData = CalculateOverlap();

            // Check for overlapping objects except our own Layer and Hands.
            var colliderCount = Physics.OverlapCapsuleNonAlloc(
                overlapData.OverlapPoint0,
                overlapData.OverlapPoint1,
                overlapData.OverlapRadius,
                _overlapColliders,
                // FIXME - It could be, that we need to do 1 << Constants.VobItemNoWorldCollision | 1 << Constants.HandLayer. Check with other Physics.*() calls.
                Constants.VobItemNoWorldCollision | Constants.HandLayer);

            return colliderCount > 0;
        }

        /// <summary>
        /// Calculate OverlapCapsule() information based on bounds of MeshCollider.
        /// This is:
        /// * MainAxis - will be used for Point0 and Point1 calculation
        /// * SecondMainAxis - will be used for Capsule radius
        /// * Point0 and Point1 - calculated end-positions between min and max of mesh collider's points
        /// * Radius - used from second max axis (Third/lowest axis value isn't needed as it is always included in the radius dimension)
        /// * Center - used from bounds center
        /// </summary>
        private OverlapCheckData CalculateOverlap()
        {
            var result = new OverlapCheckData();
            var mainBounds = _meshCollider.bounds;

            result.Center = mainBounds.center;

            if (mainBounds.size.x > mainBounds.size.y)
            {
                if (mainBounds.size.x > mainBounds.size.z)
                {
                    result.MaxAxis = Axis.X;
                    result.SecondMaxAxis = mainBounds.size.y > mainBounds.size.z ? Axis.Y : Axis.Z;
                }
                else
                {
                    result.MaxAxis = Axis.Z;
                    result.SecondMaxAxis = Axis.X;
                }
            }
            else
            {
                if (mainBounds.size.y > mainBounds.size.z)
                {
                    result.MaxAxis = Axis.Y;
                    result.SecondMaxAxis = mainBounds.size.x > mainBounds.size.z ? Axis.X : Axis.Z;
                }
                else
                {
                    result.MaxAxis = Axis.Z;
                    result.SecondMaxAxis = Axis.Y;
                }
            }

            switch (result.MaxAxis)
            {
                case Axis.X:
                    result.OverlapPoint0 = mainBounds.center - new Vector3(mainBounds.size.x / 2, 0, 0);
                    result.OverlapPoint1 = mainBounds.center + new Vector3(mainBounds.size.x / 2, 0, 0);
                    break;
                case Axis.Y:
                    result.OverlapPoint0 = mainBounds.center - new Vector3(0, mainBounds.size.y / 2, 0);
                    result.OverlapPoint1 = mainBounds.center + new Vector3(0, mainBounds.size.y / 2, 0);
                    break;
                default:
                    result.OverlapPoint0 = mainBounds.center - new Vector3(0, 0, mainBounds.size.z / 2);
                    result.OverlapPoint1 = mainBounds.center + new Vector3(0, 0, mainBounds.size.z / 2);
                    break;
            }

            switch (result.SecondMaxAxis)
            {
                case Axis.X:
                    result.OverlapRadius = mainBounds.size.x / 2;
                    break;
                case Axis.Y:
                    result.OverlapRadius = mainBounds.size.y / 2;
                    break;
                default:
                    result.OverlapRadius = mainBounds.size.z / 2;
                    break;
            }

            return result;
        }

        /// <summary>
        /// Reset everything (e.g., when GO is culled out.)
        /// </summary>
        private void OnDisable()
        {
            _dynamicMaterialService.ResetAllDynamicValues(gameObject);
        }
    }
}
#endif
