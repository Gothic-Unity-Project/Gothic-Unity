using System;
using System.Collections;
using Gothic.Core.Adapters.Properties.Vobs;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Const;
using Gothic.Core.Logging;
using Gothic.Core.Models.Vm;
using Gothic.Core.Manager;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Culling;
using Gothic.Core.Services.Meshes;
using Gothic.Core.Services.World;
using Gothic.Core.Adapters.Vob;
using Gothic.VR.Services;
using Gothic.Core;
using Gothic.Core.Services;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Models;
using Gothic.Core.Extensions;
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
        [Inject] private readonly DocService _docService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly Gothic.Core.Services.Caches.ResourceCacheService _resourceCacheService;
        [Inject] private readonly Gothic.Core.Services.Vobs.VobService _vobService;
        [Inject] private readonly Gothic.Core.Services.Player.PlayerService _playerService;

        // A sealed letter (MAPSEALED) is used up when it's read - but only when the reading ends (one hand lets go),
        // not while it's still in front of the player.
        private bool _isConsumedOnDocClose;
        [Inject] private readonly MarvinService _marvinService;
        [Inject] private readonly Gothic.Core.Services.Npc.FightService _fightService;
        [Inject] private readonly DynamicMaterialService _dynamicMaterialService;
        [Inject] private readonly VobMeshCullingService _vobMeshCullingService;
        [Inject] private readonly SaveGameService _saveGameService;

        [SerializeField] private VRVobItemProperties _vrProperties;
        [SerializeField] private Rigidbody _rigidbody;
        [SerializeField] private MeshCollider _meshCollider;

        // We pre-allocate enough entries to fetch at least one entry which is not ourselves.
        private readonly Collider[] _overlapColliders = new Collider[1];
        private static int _ignoreLayerCollisionCheck;

        private VrInteractable _interactable;
        private Coroutine _reEnableCollisionRoutine;


        private struct OverlapCheckData
        {
            public Axis MaxAxis; // Used for P0 and P1 calculation
            public Axis SecondMaxAxis; // Used for radius calculation
            public Vector3 Center;
            public Vector3 OverlapPoint0; // First point where the sphere will start to draw its closing circle

            public Vector3
                OverlapPoint1; // Second point with the closing circle calculation. Will be opposite of first one based on mainAxis

            public float OverlapRadius; // Radius is calculated as /2 calculation from size of secondMaxAxis
        }


        private void Awake()
        {
            // Pre-fill calculation data before being used every frame.
            if (_ignoreLayerCollisionCheck == 0)
            {
                _ignoreLayerCollisionCheck = Constants.VobItemNoWorldCollision | Constants.HandLayer;
            }

            _interactable = GetComponent<VrInteractable>();
            _interactable.Grabbed += OnGrabbed;
            _interactable.Released += OnReleased;
        }

        private void Start()
        {
            _interactable.SetupColliders();
        }

        private void OnDestroy()
        {
            if (_interactable == null)
                return;

            _vrPlayerService?.ForgetPlayerCollision(_interactable);
            _interactable.Grabbed -= OnGrabbed;
            _interactable.Released -= OnReleased;
        }

        /// <summary>
        /// 1. Grabbed via ForceGrabber (remote grabbing into hand)
        /// 2. Grabbed via HandGrabber (direct grabbing into hand)
        /// 3. Grabbed via Socket (e.g., backpack)
        /// </summary>
        private void OnGrabbed(VrGrabArgs args)
        {
            // Ignore grabbing once, if MarvinSelectionMode is active.
            if (_marvinService.IsMarvinSelectionMode)
            {
                _marvinService.MarvinSelectionGO = args.Interactable.gameObject;
                return;
            }

            // Held and socketed items must not push our own CharacterController or the hand holding them.
            _vrPlayerService.UpdatePlayerCollision(args, true);

            // OnGrabbed is normally called multiple times. Even after an object is already socketed. If so, then let's stop Grab behaviour.
            // If we sock an object on our hips or backpack etc.
            if (args.Kind == VrGrabberKind.Socket)
            {
                SetSocketedLayer();
                // Out of a holster/backpack a crossbow is cocked.
                if (TryGetComponent<VobItem.VRCrossbowString>(out var crossbowString))
                    crossbowString.SetProgress(1f);
                return;
            }

            // Stop collisions while being dragged around (at least shortly; otherwise e.g. items might stick inside chests when pulled out).
            gameObject.layer = Constants.VobItemNoWorldCollision;

            // At least until object isn't colliding with anything any longer, the object will be a ghost (i.e. no collision + transparency activated)
            _dynamicMaterialService.SetDynamicValue(gameObject, Constants.ShaderPropertyTransparency,
                Constants.ShaderPropertyTransparencyValue);

            // If we want Item collisions, we just temporarily deactivate them until the item is free of collisions.
            StopReEnableCollisionRoutine();
            _reEnableCollisionRoutine = StartCoroutine(ReEnableCollisionRoutine());

            _vobMeshCullingService?.StartTrackVobPositionUpdates(gameObject);
            _vrPlayerService.SetGrab(args);

            // The engine's "missing ... points" (G_CanNotUse) for a weapon the hero can't use - a melee weapon hits
            // for 1, a bow/crossbow doesn't shoot.
            TryShowWeaponRequirement();

            // One hand is enough for ranged weapons.
            TryPrepareRangedWeapon();

            // Both hands on a usable item (armor, potion, pouch...): VRItemUser waits for the trigger.
            if (_vrPlayerService.IsDualGrabbed)
            {
                TryShowDocument();
                TryCastSpell();
                TryPrepareItemUse();
            }
            // An item lying on another VOB (e.g. a mover) is no longer part of it.
            var vobContainer = GetComponentInParent<VobLoader>()?.Container;
            if (vobContainer != null)
                _saveGameService.DetachChildVob(vobContainer);
        }

        /// <summary>
        /// 1. Grabbed via ForceGrabber (remote grabbing into hand)
        /// 2. Grabbed via HandGrabber (direct grabbing into hand)
        /// 3. Grabbed via Socket (e.g., backpack)
        /// </summary>
        private void OnReleased(VrGrabArgs args)
        {
            // Collisions come back for whatever doesn't hold the item any longer (e.g. the releasing hand).
            _vrPlayerService.UpdatePlayerCollision(args, false);

            // If we release an object from our hips or backpack etc.
            // We then assume, that the item is already with correct values as it was grabbed before (shader, ...).
            // A hand taking it out sets its own layer right after (OnGrabbed).
            if (args.Kind == VrGrabberKind.Socket)
            {
                if (gameObject.layer == Constants.VobItemSocketedLayer)
                    gameObject.layer = Constants.VobItemLayer;
                return;
            }

            gameObject.layer = Constants.VobItemLayer; // Back to default

            // Disable "ghostification" of object.
            _dynamicMaterialService.ResetDynamicValue(gameObject, Constants.ShaderPropertyTransparency,
                Constants.ShaderPropertyTransparencyDefault);

            _vobMeshCullingService?.StopTrackVobPositionUpdates(gameObject);
            _vrPlayerService.UnsetGrab(args);

            // Bows/crossbows shoot as long as any hand holds them. Removed immediately: a release + grab in the same
            // frame (holster -> hand, hand -> hand) found the component still pending destruction and added no new
            // one - the bow ended up without its string.
            var isStillHeld = _vrPlayerService.GrabbedItemLeft == gameObject ||
                              _vrPlayerService.GrabbedItemRight == gameObject;
            if (!isStillHeld && TryGetComponent<VobItem.VRCrossbow>(out var crossbow))
                DestroyImmediate(crossbow);
            if (!isStillHeld && TryGetComponent<VobItem.VRBow>(out var bow))
                DestroyImmediate(bow);

            if (_vrPlayerService.IsDualGrabbed)
                return;

            // The document viewer closes when the item is no longer dual-grabbed.
            if (TryGetComponent<VobItem.VRDocViewer>(out var viewer))
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

            if (TryGetComponent<VobItem.VRItemUser>(out var itemUser))
                Destroy(itemUser);

            if (TryGetComponent<VobItem.VRRuneCaster>(out var caster))
            {
                // During telekinesis, aiming or a throwable spell, the caster stays while one hand still holds the
                // rune - the freed hand grabs the distant item, throwable spells are held in one hand by design.
                var isRuneStillHeld = _vrPlayerService.GrabbedItemLeft == gameObject ||
                                      _vrPlayerService.GrabbedItemRight == gameObject;
                var isKeptAlive = isRuneStillHeld &&
                                  (_vrPlayerService.IsTelekinesisActive || caster.IsTargetingActive || caster.IsThrowable);
                if (!isKeptAlive)
                    Destroy(caster);
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
                if (Adapters.Vob.VobItem.VRItemUser.IsConsumedOnUse(item, _resourceCacheService))
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
            var container = GetComponentInParent<VobLoader>()?.Container;
            if (container?.Vob is not ZenKit.Vobs.IItem vobItem)
                return;

            var instanceName = vobItem.GetSymbolName();
            if (vobItem.Amount > 1)
            {
                vobItem.Amount--;
                if (_interactable != null && _interactable.IsHeld)
                    _playerService.RemoveItem(instanceName, 1);
                Logger.Log($"[VRVobItem] {instanceName} read - one used up, remaining={vobItem.Amount}", LogCat.VR);
                return;
            }

            Logger.Log($"[VRVobItem] {instanceName} read - used up", LogCat.VR);
            if (_interactable != null && _interactable.IsHeld)
                _interactable.ForceRelease();
            _vobService.RemoveWorldItem(container);
        }


        /// <summary>
        /// Runes and scrolls (ITEM_KAT_RUNE) held with both hands cast (VRRuneCaster).
        /// </summary>
        private void TryCastSpell()
        {
            var item = GetComponentInParent<VobLoader>()?.Container.PropsAs<VobItemProperties2>()?.Instance;
            if (item == null || (VmGothicEnums.ItemFlags)item.MainFlag != VmGothicEnums.ItemFlags.ItemKatRune)
                return;

            if (GetComponent<VobItem.VRRuneCaster>() == null)
                gameObject.AddComponent<VobItem.VRRuneCaster>();
        }

        /// <summary>
        /// DeveloperConfig.EnableVrCrossbow / EnableVrBows: ranged weapons shoot while held.
        /// </summary>
        private void TryPrepareRangedWeapon()
        {
            if (GetComponent<VobItem.VRCrossbow>() != null || GetComponent<VobItem.VRBow>() != null)
                return;

            var item = GetComponentInParent<VobLoader>()?.Container.PropsAs<VobItemProperties2>()?.Instance;
            if (!item.IsRangedWeapon())
                return;

            if (item.IsCrossbow())
            {
                if (_configService.Dev.EnableVrCrossbow)
                    gameObject.AddComponent<VobItem.VRCrossbow>();
            }
            else if (_configService.Dev.EnableVrBows)
                gameObject.AddComponent<VobItem.VRBow>();
        }

        /// <summary>
        /// Items with a "use" function (on_state[0]) like pouches, potions and food, and wearables (armor, amulets,
        /// rings, belts). VRItemUser waits for the trigger while dual-grabbed.
        /// </summary>
        private void TryPrepareItemUse()
        {
            var item = GetComponentInParent<VobLoader>()?.Container
                .PropsAs<VobItemProperties2>()?.Instance;
            if (!VobItem.VRItemUser.IsUsable(item, _configService.Dev.EnableEquipItems))
                return;

            if (GetComponent<VobItem.VRItemUser>() == null)
                gameObject.AddComponent<VobItem.VRItemUser>();
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
                    Gizmos.DrawLine(overlapData.Center,
                        overlapData.Center + new Vector3(overlapData.OverlapRadius, 0, 0));
                    break;
                case Axis.Y:
                    Gizmos.DrawLine(overlapData.Center,
                        overlapData.Center + new Vector3(0, overlapData.OverlapRadius, 0));
                    break;
                default:
                    Gizmos.DrawLine(overlapData.Center,
                        overlapData.Center + new Vector3(0, 0, overlapData.OverlapRadius));
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
        /// Nothing collides with an item in a slot (hands, body, the head-down ground check), but the hands' near grab
        /// still finds it. Placed right after a hand let go, the hand grab's ghost phase must not end on VobItem.
        /// </summary>
        private void SetSocketedLayer()
        {
            StopReEnableCollisionRoutine();
            gameObject.layer = Constants.VobItemSocketedLayer;
            _dynamicMaterialService.ResetDynamicValue(gameObject, Constants.ShaderPropertyTransparency,
                Constants.ShaderPropertyTransparencyDefault);
        }

        private void StopReEnableCollisionRoutine()
        {
            if (_reEnableCollisionRoutine == null)
                return;

            StopCoroutine(_reEnableCollisionRoutine);
            _reEnableCollisionRoutine = null;
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

            _reEnableCollisionRoutine = null;

            // Re-enable collisions
            gameObject.layer = Constants.VobItemLayer;

            // Disable "ghostification" of object.
            _dynamicMaterialService.ResetDynamicValue(gameObject, Constants.ShaderPropertyTransparency,
                Constants.ShaderPropertyTransparencyDefault);
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

        private void TryShowWeaponRequirement()
        {
            var container = GetComponentInParent<VobLoader>()?.Container;
            if (container != null && container.Vob.Type == ZenKit.Vobs.VirtualObjectType.oCItem &&
                (container.IsMeleeWeapon() || container.IsRangedWeapon()))
                _fightService.CheckHeroWeaponRequirement(container.GetItemInstance(), isMessageShown: true);
        }
    }
}
