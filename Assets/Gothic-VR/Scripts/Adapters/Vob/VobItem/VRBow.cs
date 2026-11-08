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
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Interaction.Components;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Vob.VobItem
{
    /// <summary>
    /// VR bow (ADR 2026-11-02-vr-ranged): the Gothic bow mesh keeps its limbs,
    /// its own string triangles are cut out and replaced by our string (top limb tip -> nock -> bottom limb tip).
    /// - Hold the bow in one hand. Put the other, empty hand to the string and hold its grip to draw.
    ///   VrInteractable.CanSecondHandGrabAt keeps that hand from grabbing the bow. An arrow (the bow's munition) is
    ///   nocked if the hero has one.
    /// - The other hand can also grab the bow itself (limbs/grip) to hold it with both hands - only near the string it
    ///   draws instead.
    /// - Release the grip to shoot along the line drawing hand -> bow hand: speed and damage grow with the draw.
    /// - If the bow has its Gothic morph animation (S_SHOOT), the limbs bend with the draw.
    /// Everything is calibrated from the mesh (tips = ends of the long axis, grip = farthest vertex from the string),
    /// once on the first grab. The component stays on the bow; VRVobItem switches it with SetHeld (disabled = not held:
    /// the Gothic mesh and string are back).
    /// </summary>
    public class VRBow : MonoBehaviour
    {
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly IVrInput _vrInput;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly VRWeaponService _vrWeaponService;
        [Inject] private readonly VRRangedService _vrRangedService;
        [Inject] private readonly VrHapticsService _hapticsService;
        [Inject] private readonly MorphMeshCacheService _morphMeshCacheService;
        [Inject] private readonly FightService _fightService;

        private const float _grabStringDistance = 0.25f;
        private const float _minShootTension = 0.15f;
        private const float _minSpeed = 8f;
        private const float _minDamageScale = 0.3f;
        private const float _stringWidth = 0.005f;
        private static readonly Color _stringColor = new(0.33f, 0.28f, 0.2f);
        private static Material _stringMaterial;

        private ItemInstance _item;
        private ItemInstance _munition;

        // Mesh-local, once per bow (TrySetup). The bent vertices while drawn, null at rest.
        private readonly VrBowMeshCalibration _calibration = new();
        private Vector3[] _currentVertices;

        private LineRenderer _string;
        private bool _isDrawing;
        private VrHandSide _drawHand;
        private float _tension;
        private float _lastHapticTension;
        private GameObject _arrow;
        private Vector3 _nockWorld;

        private bool _isHeld;
        private bool _isSetUp;
        private bool _isSetupFailed;

        private void Awake()
        {
            this.Inject();
            // Runs only while held (SetHeld).
            enabled = false;
        }

        /// <summary>
        /// A hand took the bow (true) or the last hand let it go (false). A second hand on the bow changes nothing.
        /// </summary>
        public void SetHeld(bool isHeld)
        {
            if (isHeld == _isHeld)
                return;

            if (isHeld)
                Hold();
            else
                LetGo();
        }

        private void Hold()
        {
            if (!TrySetup())
                return;

            _isHeld = true;
            _calibration.MeshFilter.sharedMesh = _calibration.BowMesh;
            _string.gameObject.SetActive(true);
            _nockWorld = GetRestNockWorld();
            UpdateString();
            AddStringFilter();
            Application.onBeforeRender += OnBeforeRender;
            enabled = true;
            _vrWeaponService.UpdateHeroWeaponState();

            Logger.Log($"[VRBow] {_item.Name} readied - munition={_munition?.Name ?? "none"}, " +
                       $"maxDraw={_calibration.MaxDraw:F2} m, morph frames={_calibration.DrawFrames?.Count ?? 0}, " +
                       $"forward(local)={_calibration.LocalForward}", LogCat.VR);
        }

        /// <summary>
        /// The bow goes back to the holster/backpack/world with its Gothic string.
        /// </summary>
        private void LetGo()
        {
            _isHeld = false;
            enabled = false;
            Application.onBeforeRender -= OnBeforeRender;

            _isDrawing = false;
            _tension = 0f;
            ApplyBend(0f);
            DropArrow();
            if (TryGetComponent<VrInteractable>(out var interactable))
                interactable.CanSecondHandGrabAt = null;

            _calibration.MeshFilter.sharedMesh = _calibration.OriginalMesh;
            _string.gameObject.SetActive(false);
            _vrWeaponService.UpdateHeroWeaponState();
        }

        /// <summary>
        /// Once per bow, on the first grab: calibration from the mesh, munition, our string.
        /// </summary>
        private bool TrySetup()
        {
            if (_isSetUp || _isSetupFailed)
                return _isSetUp;

            _item = GetComponentInParent<VobLoader>()?.Container?.PropsAs<VobItemProperties2>()?.Instance;
            if (_item == null || !_calibration.TrySetup(gameObject, _item, _morphMeshCacheService))
            {
                _isSetupFailed = true;
                Logger.LogWarning($"[VRBow] {_item?.Name ?? gameObject.name}: no item or no readable bow mesh - no bow.", LogCat.VR);
                return false;
            }

            _munition = _vrRangedService.GetMunition(_item);
            CreateString();
            _isSetUp = true;
            return true;
        }

        private void OnDestroy()
        {
            Application.onBeforeRender -= OnBeforeRender;
            DropArrow();
            if (_calibration.BowMesh != null)
                Destroy(_calibration.BowMesh);
            if (_string != null)
                Destroy(_string.gameObject);
        }

        private void Update()
        {
            if (!_isHeld)
                return;

            if (!_isDrawing)
                TryStartDrawing();
            else
                UpdateDrawing();
        }

        private void LateUpdate()
        {
            if (!_isHeld)
                return;

            if (!_isDrawing)
                _nockWorld = GetRestNockWorld();

            UpdateString();
            UpdateArrow();
        }

        /// <summary>
        /// XRI moves the held bow right before rendering, after LateUpdate - the string and the arrow lagged a frame
        /// behind the bow. Placed again from the bow's final pose.
        /// </summary>
        private void OnBeforeRender()
        {
            if (!_isHeld || !isActiveAndEnabled)
                return;

            if (!_isDrawing)
                _nockWorld = GetRestNockWorld();
            else
                UpdateDrawnNock();

            UpdateString();
            UpdateArrow();
        }

        private void TryStartDrawing()
        {
            if (!TryGetFreeHand(out var side))
                return;

            if (!_vrInput.GetButton(side, VrButton.Grip).IsJustActivated)
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
            PlaySfx(Constants.SfxBowReload);

            var hero = _npcService.GetHeroContainer();
            if (_munition != null && _vrRangedService.HasAmmo(hero, _munition))
                NockArrow();
        }

        private void UpdateDrawing()
        {
            var hand = _vrPlayerService.GetHandModelGo(_drawHand);
            var isHeld = _vrInput.GetButton(_drawHand, VrButton.Grip).IsActive;
            if (hand == null || !isHeld)
            {
                Release();
                return;
            }

            var drawDistance = UpdateDrawnNock();
            _tension = Mathf.Clamp01(drawDistance / _calibration.MaxDraw);
            ApplyBend(_tension);

            if (_tension - _lastHapticTension >= 0.1f)
            {
                _lastHapticTension = _tension;
                _hapticsService.Vibrate(_drawHand, VrHapticPreset.Info);
            }
        }

        /// <summary>
        /// The nock follows the drawing hand (up to the max draw). Returns how far it is drawn back.
        /// </summary>
        private float UpdateDrawnNock()
        {
            var hand = _vrPlayerService.GetHandModelGo(_drawHand);
            var restNock = GetRestNockWorld();
            if (hand == null)
            {
                _nockWorld = restNock;
                return 0f;
            }

            var forward = _calibration.MeshFilter.transform.TransformDirection(_calibration.LocalForward).normalized;
            var pull = Vector3.ClampMagnitude(hand.transform.position - restNock, _calibration.MaxDraw);
            _nockWorld = restNock + pull;
            return Mathf.Max(0f, Vector3.Dot(pull, -forward));
        }

        private void Release()
        {
            var tension = _tension;
            // Before _isDrawing=false - afterwards GetAimWorld() is the mesh grip and the aim was always the same.
            var direction = (GetAimWorld() - _nockWorld).normalized;
            _isDrawing = false;
            _tension = 0f;
            ApplyBend(0f);

            var hero = _npcService.GetHeroContainer();
            if (_arrow == null || tension < _minShootTension || hero == null || !_vrRangedService.HasAmmo(hero, _munition))
            {
                DropArrow();
                return;
            }

            // Without the bow's requirements (dexterity) it doesn't shoot.
            if (!_fightService.CheckHeroWeaponRequirement(_item, isMessageShown: true))
            {
                DropArrow();
                _hapticsService.Vibrate(_drawHand, VrHapticPreset.Error);
                return;
            }

            _vrRangedService.ConsumeAmmo(hero, _munition);

            var speed = Mathf.Lerp(_minSpeed, ProjectileService.ProjectileSpeed, tension);
            var damageScale = Mathf.Lerp(_minDamageScale, 1f, tension);
            var arrow = _arrow;
            _arrow = null;
            _vrRangedService.Shoot(hero, _item, _munition, _nockWorld, direction, speed, damageScale, arrow);

            PlaySfx(Constants.SfxBowShoot);
            _hapticsService.Vibrate(_drawHand, VrHapticPreset.Success);
            Logger.Log($"[VRBow] Shot {_munition.Name}: tension={tension:F2} speed={speed:F1} dmgScale={damageScale:F2}", LogCat.VR);
        }

        /// <summary>
        /// While the bow is held, the other hand's grip draws the string instead of grabbing the bow.
        /// </summary>
        private void AddStringFilter()
        {
            if (TryGetComponent<VrInteractable>(out var interactable))
                interactable.CanSecondHandGrabAt = position => !IsAtString(position);
        }

        private bool TryGetFreeHand(out VrHandSide side)
        {
            side = VrHandSide.Right;
            var isBowLeft = _vrPlayerService.GrabbedItemLeft == gameObject;
            var isBowRight = _vrPlayerService.GrabbedItemRight == gameObject;
            if (isBowLeft == isBowRight)
                return false; // both hands on the bow (or none)

            side = isBowLeft ? VrHandSide.Right : VrHandSide.Left;
            var otherItem = isBowLeft ? _vrPlayerService.GrabbedItemRight : _vrPlayerService.GrabbedItemLeft;
            return otherItem == null;
        }

        private void NockArrow()
        {
            DropArrow();
            _arrow = _vrRangedService.CreateAmmoVisual(_munition, null);

            // Tail at the root: the root sits on the nock and looks along the shot.
            var mesh = _arrow.transform.GetChild(0);
            var bounds = ProjectileService.GetLocalBounds(_arrow);
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

            var direction = GetAimWorld() - _nockWorld;
            if (direction.sqrMagnitude < 0.0001f)
                return;
            _arrow.transform.SetPositionAndRotation(_nockWorld, Quaternion.LookRotation(direction));
        }

        private void UpdateString()
        {
            if (_string == null)
                return;

            var vertices = _currentVertices ?? _calibration.RestVertices;
            var meshTransform = _calibration.MeshFilter.transform;
            _string.SetPosition(0, meshTransform.TransformPoint(vertices[_calibration.TipTopIndex]));
            _string.SetPosition(1, _nockWorld);
            _string.SetPosition(2, meshTransform.TransformPoint(vertices[_calibration.TipBottomIndex]));
        }

        private void ApplyBend(float tension)
        {
            if (_calibration.DrawFrames == null || _calibration.DrawFrames.Count == 0)
                return;

            if (tension <= 0f)
            {
                _currentVertices = null;
                _calibration.BowMesh.vertices = _calibration.RestVertices;
                return;
            }

            var framePosition = tension * (_calibration.DrawFrames.Count - 1);
            var frame = Mathf.FloorToInt(framePosition);
            var nextFrame = Mathf.Min(frame + 1, _calibration.DrawFrames.Count - 1);
            var blend = framePosition - frame;

            _currentVertices ??= new Vector3[_calibration.RestVertices.Length];
            var from = _calibration.DrawFrames[frame];
            var to = _calibration.DrawFrames[nextFrame];
            for (var i = 0; i < _currentVertices.Length; i++)
                _currentVertices[i] = Vector3.LerpUnclamped(from[i], to[i], blend);

            _calibration.BowMesh.vertices = _currentVertices;
            _calibration.BowMesh.RecalculateBounds();
        }

        private Vector3 GetRestNockWorld()
        {
            var vertices = _currentVertices ?? _calibration.RestVertices;
            var nockLocal = (vertices[_calibration.TipTopIndex] + vertices[_calibration.TipBottomIndex]) * 0.5f;
            return _calibration.MeshFilter.transform.TransformPoint(nockLocal);
        }

        private Vector3 GetGripWorld() => _calibration.MeshFilter.transform.TransformPoint(_calibration.LocalGrip);

        /// <summary>
        /// The arrow points from the drawing hand through the hand holding the bow (where the player aims), not
        /// through the mesh's grip vertex - Gothic bow meshes are held at different spots.
        /// </summary>
        private Vector3 GetAimWorld()
        {
            if (!_isDrawing)
                return GetGripWorld();

            var bowSide = _drawHand == VrHandSide.Left ? VrHandSide.Right : VrHandSide.Left;
            var bowHand = _vrPlayerService.GetHandModelGo(bowSide);
            return bowHand != null ? bowHand.transform.position : GetGripWorld();
        }

        /// <summary>
        /// VrInteractable.CanSecondHandGrabAt: a hand closer to the string than to the bow's grip draws, otherwise it
        /// grabs the bow.
        /// </summary>
        public bool IsAtString(Vector3 position)
        {
            if (_calibration.MeshFilter == null)
                return false;

            var vertices = _currentVertices ?? _calibration.RestVertices;
            var meshTransform = _calibration.MeshFilter.transform;
            var top = meshTransform.TransformPoint(vertices[_calibration.TipTopIndex]);
            var bottom = meshTransform.TransformPoint(vertices[_calibration.TipBottomIndex]);
            var nock = GetRestNockWorld();
            var toString = Mathf.Min(VrBowMeshCalibration.DistanceToSegment(position, top, nock, out _),
                VrBowMeshCalibration.DistanceToSegment(position, nock, bottom, out _));
            return toString < _grabStringDistance && toString < Vector3.Distance(position, GetGripWorld());
        }

        private void CreateString()
        {
            if (_stringMaterial == null)
            {
                var shader = Constants.ShaderUnlit != null ? Constants.ShaderUnlit : Constants.ShaderSingleMeshLit;
                _stringMaterial = new Material(shader) { color = _stringColor };
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

        private void PlaySfx(string sfxName)
        {
            var clip = _audioService.GetRandomSoundClip(sfxName);
            if (clip != null)
                AudioSource.PlayClipAtPoint(clip, transform.position);
        }
    }
}
