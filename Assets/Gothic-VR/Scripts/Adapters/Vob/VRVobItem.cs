#if GUZ_HVR_INSTALLED
using System.Collections;
using GUZ.Core;
using GUZ.Core.Const;
using GUZ.Core.Manager;
using GUZ.Core.Services;
using GUZ.Core.Services.Config;
using GUZ.Core.Services.Culling;
using GUZ.Core.Services.Meshes;
using GUZ.VR.Services;
using HurricaneVR.Framework.Core;
using HurricaneVR.Framework.Core.Grabbers;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.Animations;

namespace GUZ.VR.Adapters.Vob
{
    public class VRVobItem : MonoBehaviour
    {
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly MarvinService _marvinService;
        [Inject] private readonly DynamicMaterialService _dynamicMaterialService;
        [Inject] private readonly VobMeshCullingService _vobMeshCullingService;

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
