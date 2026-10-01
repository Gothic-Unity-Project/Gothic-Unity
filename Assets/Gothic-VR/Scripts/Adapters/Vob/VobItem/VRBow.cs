#if GOTHIC_HVR_INSTALLED
using System;
using System.Collections.Generic;
using Gothic.Core.Adapters.Properties.Vobs;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Npc;
using Gothic.VR.Services;
using HurricaneVR.Framework.ControllerInput;
using HurricaneVR.Framework.Core;
using HurricaneVR.Framework.Shared;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Vob.VobItem
{
    /// <summary>
    /// V1 (DeveloperConfig.EnableVrBows), variant B of vr-ranged-spells-plan.md: the Gothic bow mesh keeps its limbs,
    /// its own string triangles are cut out and replaced by our string (top limb tip -> nock -> bottom limb tip).
    /// - Hold the bow in one hand. Put the other, empty hand to the string and hold its grip to draw. VRBowStringFilter
    ///   stops HVR from grabbing the bow with that hand. An arrow (the bow's munition) is nocked if the hero has one.
    /// - Release the grip to shoot: speed and damage grow with the draw (VRRangedService, RangedHit).
    /// - If the bow has its Gothic morph animation (S_SHOOT), the limbs bend with the draw.
    /// Everything is calibrated from the mesh (tips = ends of the long axis, grip = farthest vertex from the string).
    /// </summary>
    public class VRBow : MonoBehaviour
    {
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly VRWeaponService _vrWeaponService;
        [Inject] private readonly VRRangedService _vrRangedService;
        [Inject] private readonly VrHapticsService _hapticsService;
        [Inject] private readonly MorphMeshCacheService _morphMeshCacheService;

        private const string _shootSfx = "BowShoot";
        private const string _drawSfx = "BowReload";
        private const float _grabStringDistance = 0.25f;
        private const float _minShootTension = 0.15f;
        private const float _minSpeed = 8f;
        private const float _minDamageScale = 0.3f;
        private const float _stringWidth = 0.005f;
        private static readonly Color _stringColor = new(0.33f, 0.28f, 0.2f);
        private static Material _stringMaterial;

        private ItemInstance _item;
        private ItemInstance _munition;

        private MeshFilter _meshFilter;
        private Mesh _originalMesh;
        private Mesh _bowMesh;
        private Vector3[] _restVertices;
        private Vector3[] _currentVertices;
        private List<Vector3[]> _drawFrames;

        // Mesh-local calibration.
        private int _tipTopIndex;
        private int _tipBottomIndex;
        private Vector3 _localGrip;
        private Vector3 _localForward;
        private float _maxDraw;

        private LineRenderer _string;
        private bool _isDrawing;
        private HVRHandSide _drawHand;
        private float _tension;
        private float _lastHapticTension;
        private GameObject _arrow;
        private Vector3 _nockWorld;


        private void Awake()
        {
            this.Inject();
        }

        private void Start()
        {
            _item = GetComponentInParent<VobLoader>()?.Container?.PropsAs<VobItemProperties2>()?.Instance;
            if (_item == null || !TrySetupMesh())
            {
                Logger.LogWarning($"[VRBow] {_item?.Name ?? gameObject.name}: no item or no readable bow mesh - no bow.", LogCat.VR);
                enabled = false;
                return;
            }

            _munition = _vrRangedService.GetMunition(_item);
            CreateString();
            AddStringFilter();
            _vrWeaponService.SetRangedReadied(VmGothicEnums.WeaponState.Bow);

            Logger.Log($"[VRBow] {_item.Name} readied - munition={_munition?.Name ?? "none"}, maxDraw={_maxDraw:F2} m, " +
                       $"morph frames={_drawFrames?.Count ?? 0}, forward(local)={_localForward}", LogCat.VR);
        }

        private void OnDestroy()
        {
            if (_item == null)
                return;

            _vrWeaponService.SetRangedReadied(null);
            DropArrow();

            // The bow goes back to the holster/backpack with its Gothic string.
            if (_meshFilter != null && _originalMesh != null)
                _meshFilter.sharedMesh = _originalMesh;
            if (_bowMesh != null)
                Destroy(_bowMesh);
            if (_string != null)
                Destroy(_string.gameObject);
        }

        private void Update()
        {
            if (_item == null)
                return;

            if (!_isDrawing)
                TryStartDrawing();
            else
                UpdateDrawing();
        }

        private void LateUpdate()
        {
            if (_item == null)
                return;

            if (!_isDrawing)
                _nockWorld = GetRestNockWorld();

            UpdateString();
            UpdateArrow();
        }

        private void TryStartDrawing()
        {
            if (!TryGetFreeHand(out var side))
                return;

            if (!HVRController.GetButtonState(side, HVRButtons.Grip).JustActivated)
                return;

            var hand = _vrPlayerService.GetHandModelGo(side);
            if (hand == null || Vector3.Distance(hand.transform.position, GetRestNockWorld()) > _grabStringDistance)
                return;

            // No shooting while knocked out.
            if (_npcService.GetHeroContainer()?.Props.BodyState == VmGothicEnums.BodyState.BsUnconscious)
                return;

            _isDrawing = true;
            _drawHand = side;
            _lastHapticTension = 0f;
            PlaySfx(_drawSfx);

            var hero = _npcService.GetHeroContainer();
            if (_munition != null && _vrRangedService.HasAmmo(hero, _munition))
                NockArrow();
        }

        private void UpdateDrawing()
        {
            var hand = _vrPlayerService.GetHandModelGo(_drawHand);
            var isHeld = HVRController.GetButtonState(_drawHand, HVRButtons.Grip).Active;
            if (hand == null || !isHeld)
            {
                Release();
                return;
            }

            var restNock = GetRestNockWorld();
            var forward = _meshFilter.transform.TransformDirection(_localForward).normalized;
            var pull = Vector3.ClampMagnitude(hand.transform.position - restNock, _maxDraw);
            var drawDistance = Mathf.Max(0f, Vector3.Dot(pull, -forward));

            _nockWorld = restNock + pull;
            _tension = Mathf.Clamp01(drawDistance / _maxDraw);
            ApplyBend(_tension);

            if (_tension - _lastHapticTension >= 0.1f)
            {
                _lastHapticTension = _tension;
                _hapticsService.Vibrate(_drawHand, VrHapticsService.VibrationType.Info);
            }
        }

        private void Release()
        {
            var tension = _tension;
            _isDrawing = false;
            _tension = 0f;
            ApplyBend(0f);

            var hero = _npcService.GetHeroContainer();
            if (_arrow == null || tension < _minShootTension || hero == null || !_vrRangedService.HasAmmo(hero, _munition))
            {
                DropArrow();
                return;
            }

            _vrRangedService.ConsumeAmmo(hero, _munition);

            var direction = (GetGripWorld() - _nockWorld).normalized;
            var speed = Mathf.Lerp(_minSpeed, VRRangedService.ProjectileSpeed, tension);
            var damageScale = Mathf.Lerp(_minDamageScale, 1f, tension);
            var arrow = _arrow;
            _arrow = null;
            _vrRangedService.Shoot(hero, _item, _munition, _nockWorld, direction, speed, damageScale, arrow);

            PlaySfx(_shootSfx);
            _hapticsService.Vibrate(_drawHand, VrHapticsService.VibrationType.Success);
            Logger.Log($"[VRBow] Shot {_munition.Name}: tension={tension:F2} speed={speed:F1} dmgScale={damageScale:F2}", LogCat.VR);
        }

        /// <summary>
        /// While the bow is held, the other hand's grip draws the string instead of grabbing the bow (see VRBowStringFilter).
        /// The filter stays on the item and is only active while a VRBow exists.
        /// </summary>
        private void AddStringFilter()
        {
            if (!TryGetComponent<HVRGrabbable>(out var grabbable) || TryGetComponent<VRBowStringFilter>(out _))
                return;

            gameObject.AddComponent<VRBowStringFilter>().Grabbable = grabbable;
            grabbable.LoadFilters();
        }

        private bool TryGetFreeHand(out HVRHandSide side)
        {
            side = HVRHandSide.Right;
            var isBowLeft = _vrPlayerService.GrabbedItemLeft == gameObject;
            var isBowRight = _vrPlayerService.GrabbedItemRight == gameObject;
            if (isBowLeft == isBowRight)
                return false; // both hands on the bow (or none)

            side = isBowLeft ? HVRHandSide.Right : HVRHandSide.Left;
            var otherItem = isBowLeft ? _vrPlayerService.GrabbedItemRight : _vrPlayerService.GrabbedItemLeft;
            return otherItem == null;
        }

        private void NockArrow()
        {
            DropArrow();
            _arrow = _vrRangedService.CreateAmmoVisual(_munition, null);

            // Tail at the root: the root sits on the nock and looks along the shot.
            var mesh = _arrow.transform.GetChild(0);
            var bounds = VRRangedService.GetLocalBounds(_arrow);
            mesh.localPosition -= new Vector3(0f, 0f, bounds.min.z);
        }

        private void DropArrow()
        {
            if (_arrow != null)
                Destroy(_arrow);
            _arrow = null;
        }

        private void UpdateArrow()
        {
            if (_arrow == null)
                return;

            var direction = GetGripWorld() - _nockWorld;
            if (direction.sqrMagnitude < 0.0001f)
                return;
            _arrow.transform.SetPositionAndRotation(_nockWorld, Quaternion.LookRotation(direction));
        }

        private void UpdateString()
        {
            if (_string == null)
                return;

            var vertices = _currentVertices ?? _restVertices;
            var meshTransform = _meshFilter.transform;
            _string.SetPosition(0, meshTransform.TransformPoint(vertices[_tipTopIndex]));
            _string.SetPosition(1, _nockWorld);
            _string.SetPosition(2, meshTransform.TransformPoint(vertices[_tipBottomIndex]));
        }

        private void ApplyBend(float tension)
        {
            if (_drawFrames == null || _drawFrames.Count == 0)
                return;

            if (tension <= 0f)
            {
                _currentVertices = null;
                _bowMesh.vertices = _restVertices;
                return;
            }

            var framePosition = tension * (_drawFrames.Count - 1);
            var frame = Mathf.FloorToInt(framePosition);
            var nextFrame = Mathf.Min(frame + 1, _drawFrames.Count - 1);
            var blend = framePosition - frame;

            _currentVertices ??= new Vector3[_restVertices.Length];
            var from = _drawFrames[frame];
            var to = _drawFrames[nextFrame];
            for (var i = 0; i < _currentVertices.Length; i++)
                _currentVertices[i] = Vector3.LerpUnclamped(from[i], to[i], blend);

            _bowMesh.vertices = _currentVertices;
            _bowMesh.RecalculateBounds();
        }

        private Vector3 GetRestNockWorld()
        {
            var vertices = _currentVertices ?? _restVertices;
            var nockLocal = (vertices[_tipTopIndex] + vertices[_tipBottomIndex]) * 0.5f;
            return _meshFilter.transform.TransformPoint(nockLocal);
        }

        private Vector3 GetGripWorld() => _meshFilter.transform.TransformPoint(_localGrip);

        /// <summary>
        /// Finds the bow mesh, the limb tips (ends of the long axis), the grip (farthest from the string) and cuts the
        /// Gothic string out of a mesh copy.
        /// </summary>
        private bool TrySetupMesh()
        {
            foreach (var meshFilter in GetComponentsInChildren<MeshFilter>(true))
            {
                if (meshFilter.sharedMesh != null && meshFilter.sharedMesh.isReadable && meshFilter.sharedMesh.vertexCount > 0)
                {
                    _meshFilter = meshFilter;
                    break;
                }
            }
            if (_meshFilter == null)
                return false;

            _originalMesh = _meshFilter.sharedMesh;
            _restVertices = _originalMesh.vertices;

            var bounds = _originalMesh.bounds;
            var size = bounds.size;
            var axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;

            _tipTopIndex = 0;
            _tipBottomIndex = 0;
            for (var i = 1; i < _restVertices.Length; i++)
            {
                if (_restVertices[i][axis] > _restVertices[_tipTopIndex][axis])
                    _tipTopIndex = i;
                if (_restVertices[i][axis] < _restVertices[_tipBottomIndex][axis])
                    _tipBottomIndex = i;
            }

            var tipTop = _restVertices[_tipTopIndex];
            var tipBottom = _restVertices[_tipBottomIndex];
            var bowLength = Vector3.Distance(tipTop, tipBottom);
            if (bowLength < 0.1f)
                return false;

            // Grip/belly: the vertex farthest away from the string line.
            var farthest = 0f;
            _localGrip = bounds.center;
            foreach (var vertex in _restVertices)
            {
                var distance = DistanceToSegment(vertex, tipTop, tipBottom, out _);
                if (distance > farthest)
                {
                    farthest = distance;
                    _localGrip = vertex;
                }
            }
            DistanceToSegment(_localGrip, tipTop, tipBottom, out var gripOnString);
            _localForward = (_localGrip - gripOnString).normalized;

            var worldScale = _meshFilter.transform.lossyScale.x;
            _maxDraw = Mathf.Clamp(bowLength * worldScale * 0.55f, 0.45f, 0.72f);

            _bowMesh = Instantiate(_originalMesh);
            _bowMesh.name = $"{_originalMesh.name} (VR bow)";
            _bowMesh.MarkDynamic();
            var removed = RemoveStringTriangles(_bowMesh, tipTop, tipBottom, bowLength);
            _meshFilter.sharedMesh = _bowMesh;

            _drawFrames = TryGetDrawFrames();
            Logger.Log($"[VRBow] {_item.Name}: length={bowLength:F2}, string triangles removed={removed}", LogCat.VR);
            return true;
        }

        /// <summary>
        /// The Gothic string = triangles lying on the line between the limb tips (not at the tips themselves).
        /// </summary>
        private int RemoveStringTriangles(Mesh mesh, Vector3 tipTop, Vector3 tipBottom, float bowLength)
        {
            var maxDistance = Mathf.Max(0.005f, bowLength * 0.015f);
            var removed = 0;

            for (var subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
            {
                var triangles = mesh.GetTriangles(subMesh);
                var kept = new List<int>(triangles.Length);
                for (var i = 0; i + 2 < triangles.Length; i += 3)
                {
                    var a = _restVertices[triangles[i]];
                    var b = _restVertices[triangles[i + 1]];
                    var c = _restVertices[triangles[i + 2]];
                    DistanceToSegment((a + b + c) / 3f, tipTop, tipBottom, out _, out var t);

                    var isString = t > 0.08f && t < 0.92f &&
                                   DistanceToSegment(a, tipTop, tipBottom, out _) < maxDistance &&
                                   DistanceToSegment(b, tipTop, tipBottom, out _) < maxDistance &&
                                   DistanceToSegment(c, tipTop, tipBottom, out _) < maxDistance;
                    if (isString)
                    {
                        removed++;
                        continue;
                    }

                    kept.Add(triangles[i]);
                    kept.Add(triangles[i + 1]);
                    kept.Add(triangles[i + 2]);
                }
                mesh.SetTriangles(kept, subMesh);
            }

            return removed;
        }

        /// <summary>
        /// Gothic bows: morphAni "S_SHOOT" = frames 1-12 of the draw (ITRW_BOW_*.MMS). Only if the cached vertex layout
        /// matches this mesh - otherwise the limbs simply don't bend.
        /// </summary>
        private List<Vector3[]> TryGetDrawFrames()
        {
            try
            {
                var frames = _morphMeshCacheService.TryGetMorphData(_item.Visual, "S_SHOOT");
                if (frames == null || frames.Count == 0 || frames[0].Length != _restVertices.Length)
                    return null;
                return frames;
            }
            catch (Exception e)
            {
                Logger.LogWarning($"[VRBow] {_item.Visual}: no S_SHOOT morph ({e.Message}) - limbs stay straight.", LogCat.VR);
                return null;
            }
        }

        private void CreateString()
        {
            if (_stringMaterial == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                _stringMaterial = new Material(shader != null ? shader : Constants.ShaderSingleMeshLit) { color = _stringColor };
            }

            var stringGo = new GameObject("_BowString");
            stringGo.transform.SetParent(transform, false);
            _string = stringGo.AddComponent<LineRenderer>();
            _string.useWorldSpace = true;
            _string.positionCount = 3;
            _string.widthMultiplier = _stringWidth;
            _string.sharedMaterial = _stringMaterial;
            _string.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _string.receiveShadows = false;
            _nockWorld = GetRestNockWorld();
            UpdateString();
        }

        private static float DistanceToSegment(Vector3 point, Vector3 a, Vector3 b, out Vector3 closest)
        {
            return DistanceToSegment(point, a, b, out closest, out _);
        }

        private static float DistanceToSegment(Vector3 point, Vector3 a, Vector3 b, out Vector3 closest, out float t)
        {
            var ab = b - a;
            t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / ab.sqrMagnitude);
            closest = a + ab * t;
            return Vector3.Distance(point, closest);
        }

        private void PlaySfx(string sfxName)
        {
            var clip = _audioService.GetRandomSoundClip(sfxName);
            if (clip != null)
                AudioSource.PlayClipAtPoint(clip, transform.position);
        }
    }
}
#endif
