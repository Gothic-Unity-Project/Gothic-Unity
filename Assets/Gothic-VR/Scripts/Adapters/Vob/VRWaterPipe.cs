using System;
using System.Collections;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Player;
using Gothic.Core.Services.Vobs;
using Gothic.VR.Adapters.Vob.VobItem;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Services;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Vob
{
    /// <summary>
    /// A water pipe's mouthpiece in the hero's hand (Docs/mobs.md, ADR 2026-11-26-vr-water-pipe). Added by
    /// VRMobInteraction to a mob whose model ends a hose (BIP01 FLEX0..BIP01 FLEX4) in BIP01 FLEXEND - mods' pipes
    /// with these nodes work the same:
    /// - A small grab trigger on the mouthpiece is a handle of its own (IVrBackend.AddHandle). The hand holds it, the
    ///   hose's links bend towards the hand (HoseIk) - the model's own nodes, no physics.
    /// - Pulled further than the hose is long (plus a little slack), the mouthpiece slips out of the hand.
    /// - Let go, the links swing back to their rest pose (or to the pose the pipe's own animation gives them).
    /// - Busy while an NPC smokes it (a living NPC uses it or walks to it): checked when a hand hovers or grabs it.
    /// - At the mouth (VRMouth, IVrMouthSmokable) the pipe works like for an NPC (MobAnimationService.StartHeroUse:
    ///   PIPE_BUBBLES, FIRE_SMOKE at the bowl) and after a drag the hero blows out LIGHTSMOKE (HUMANS S_SMOKE_S1).
    ///   Away from the mouth or let go, it rests again (StopHeroUse). Like in the original, nothing else happens: no
    ///   item is used, no script runs.
    /// Nothing is saved: the pipe rests again after a load.
    ///
    /// LateUpdate runs only while the mouthpiece is held or swings back, else the component is disabled.
    /// </summary>
    public class VRWaterPipe : MonoBehaviour, IVrMouthSmokable
    {
        private const string _mouthpieceNodeName = "BIP01 FLEXEND";
        private static readonly string[] _hoseNodeNames =
        {
            "BIP01 FLEX0", "BIP01 FLEX1", "BIP01 FLEX2", "BIP01 FLEX3", "BIP01 FLEX4"
        };

        private const string _handleName = "MouthpieceHandle";
        private const float _handleRadius = 0.15f;
        // The hand may be this much further from the hose's start than the hose is long before it slips out.
        private const float _slack = 0.15f;
        private const float _swingBackSeconds = 0.3f;
        // A hand grabbing the pipe this close to the mouthpiece takes the mouthpiece (VRMobInteraction).
        private const float _takeOverDistance = 0.15f;
        private const int _ikIterations = 10;
        private const float _ikMaxTurnDegrees = 25f;
        private const float _ikTolerance = 0.005f;
        // The hero's smoking animation: its particle effect (LIGHTSMOKE) is the smoke blown out.
        private const string _exhaleAnimationName = "S_SMOKE_S1";

        [Inject] private readonly IVrBackend _vrBackend;
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly VobService _vobService;
        [Inject] private readonly PlayerService _playerService;
        [Inject] private readonly MobAnimationService _mobAnimationService;

        private VobContainer _mob;
        // The mob's VOB name for the logs (this GameObject is its root node, BIP01 PIPE).
        private string _logName;
        private Transform[] _links;
        private Transform _mouthpiece;
        private VrInteractable _handle;
        private Collider _handleTrigger;

        // Rest pose of the links and the mouthpiece (the model's), baked once.
        private Vector3[] _restPositions;
        private Quaternion[] _restRotations;
        private Vector3 _mouthpieceRestPosition;
        private Quaternion _mouthpieceRestRotation;
        private float _maxReachSqr;

        // The links' local rotations: solved while held (the next frame starts from them), at the release, and last
        // written while swinging back.
        private Quaternion[] _solvedRotations;
        private Quaternion[] _releasedRotations;
        private Quaternion[] _writtenRotations;

        private bool _isHeld;
        private bool _isBusy;
        private Transform _hand;
        private Vector3 _grabOffset;
        private float _swingBack;
        private bool _isTakeOverPending;
        private bool _isReleasePending;
        private bool _isAtMouth;


        /// <summary>
        /// Adds the component if the mob's model has the hose with the mouthpiece at its end (checked once per built
        /// mob). The nodes carry their Gothic names (architecture.md rule C exception, like MobAnimator's root node).
        /// </summary>
        public static VRWaterPipe TryAdd(GameObject mobGo)
        {
            var mouthpiece = mobGo.transform.FindChildRecursively(_mouthpieceNodeName);
            if (mouthpiece == null)
                return null;

            var links = new Transform[_hoseNodeNames.Length];
            var node = mouthpiece.parent;
            for (var i = links.Length - 1; i >= 0; i--)
            {
                if (node == null || !node.name.Equals(_hoseNodeNames[i], StringComparison.OrdinalIgnoreCase))
                {
                    Logger.LogWarning($"[VRWaterPipe] {mobGo.name}: {_mouthpieceNodeName} hangs on no hose " +
                                      $"({_hoseNodeNames[i]} missing) - the mouthpiece can't be grabbed.", LogCat.VR);
                    return null;
                }

                links[i] = node;
                node = node.parent;
            }

            var pipe = mobGo.AddComponent<VRWaterPipe>().Inject();
            pipe.Init(links, mouthpiece);
            return pipe;
        }

        private void Init(Transform[] links, Transform mouthpiece)
        {
            // Only LateUpdate needs it, and only while held or swinging back.
            enabled = false;

            _mob = GetComponentInParent<VobLoader>()?.Container;
            _logName = _mob?.Vob?.Name ?? name;
            _links = links;
            _mouthpiece = mouthpiece;
            _restPositions = new Vector3[links.Length];
            _restRotations = new Quaternion[links.Length];
            _solvedRotations = new Quaternion[links.Length];
            _releasedRotations = new Quaternion[links.Length];
            _writtenRotations = new Quaternion[links.Length];

            var hoseLength = 0f;
            for (var i = 0; i < links.Length; i++)
            {
                _restPositions[i] = links[i].localPosition;
                _restRotations[i] = links[i].localRotation;
                var next = i + 1 < links.Length ? links[i + 1] : mouthpiece;
                hoseLength += Vector3.Distance(links[i].position, next.position);
            }

            _mouthpieceRestPosition = mouthpiece.localPosition;
            _mouthpieceRestRotation = mouthpiece.localRotation;
            var maxReach = hoseLength + _slack;
            _maxReachSqr = maxReach * maxReach;

            if (CreateHandle())
                Logger.LogEditor($"[VRWaterPipe] {_logName}: the mouthpiece can be grabbed (hose {hoseLength:0.00} m).",
                    LogCat.VR);
        }

        /// <summary>
        /// A trigger on the mouthpiece the hands grab - not one of the mob's own colliders, so the mob's interactable
        /// doesn't take it. Layer VobItemNoWorldCollision: the hands' near grab and VRMouth's trigger see it, the world
        /// doesn't.
        /// </summary>
        private bool CreateHandle()
        {
            var handleGo = new GameObject(_handleName);
            handleGo.SetActive(false);
            handleGo.layer = Constants.VobItemNoWorldCollision;
            handleGo.transform.SetParent(_mouthpiece, false);

            var trigger = handleGo.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = _handleRadius / Mathf.Max(0.01f, _mouthpiece.lossyScale.x);
            _handleTrigger = trigger;

            _handle = _vrBackend?.AddHandle(handleGo, trigger);
            if (_handle == null)
            {
                Logger.LogWarning($"[VRWaterPipe] {_logName}: the VR backend can't make the mouthpiece a handle - it " +
                                  "can't be grabbed.", LogCat.VR);
                Destroy(handleGo);
                return false;
            }

            _handle.CanGrab = CanGrab;
            _handle.HoverEntered += OnHoverEntered;
            _handle.Grabbed += OnGrabbed;
            _handle.Released += OnReleased;

            handleGo.Inject();
            handleGo.SetActive(true);
            return true;
        }

        private void OnDestroy()
        {
            if (_handle == null)
                return;

            _handle.CanGrab = null;
            _handle.HoverEntered -= OnHoverEntered;
            _handle.Grabbed -= OnGrabbed;
            _handle.Released -= OnReleased;
        }

        /// <summary>
        /// An NPC smokes it right now (ZS_Smoke_Waterpipe: AI_UseMob "SMOKE") - Core decides (VobService). The hero's
        /// own use (MobAnimationService.StartHeroUse) sets no user.
        /// </summary>
        private bool IsBusy()
        {
            return _vobService.IsUsedByOtherNpc(_mob, _playerService.HeroContainer);
        }

        /// <summary>
        /// VrInteractable.CanGrab: answered from the hover's check, the backend asks while a hand tries to grab.
        /// </summary>
        private bool CanGrab()
        {
            return !_isBusy;
        }

        private void OnHoverEntered(VrGrabArgs args)
        {
            _isBusy = IsBusy();
            if (_isBusy)
                Logger.LogEditor($"[VRWaterPipe] {_logName}: an NPC smokes it - the mouthpiece can't be taken.",
                    LogCat.VR);
        }

        private void OnGrabbed(VrGrabArgs args)
        {
            if (!args.IsHandOrDistance)
                return;

            // Hovered before the NPC sat down (or put into the hand by VRMobInteraction).
            if (IsBusy())
            {
                _isBusy = true;
                Logger.Log($"[VRWaterPipe] {_logName}: an NPC smokes it - the mouthpiece is let go again.", LogCat.VR);
                ReleaseAfterGrab();
                return;
            }

            _hand = _vrPlayerService.Rig?.GetHandVisual(args.HandSide);
            if (_hand == null)
            {
                Logger.LogWarning($"[VRWaterPipe] {_logName}: no {args.HandSide} hand - the mouthpiece is let go.",
                    LogCat.VR);
                ReleaseAfterGrab();
                return;
            }

            _grabOffset = _hand.InverseTransformPoint(_mouthpiece.position);
            for (var i = 0; i < _links.Length; i++)
                _solvedRotations[i] = _links[i].localRotation;

            _isHeld = true;
            enabled = true;
            Logger.Log($"[VRWaterPipe] {_logName}: mouthpiece taken ({args.HandSide} hand).", LogCat.VR);
        }

        /// <summary>
        /// A grab the pipe refuses after all: let go a frame later, not inside the backend's grab event
        /// (architecture.md "Inside Gothic-VR" 4), like TakeOverGrab.
        /// </summary>
        private void ReleaseAfterGrab()
        {
            if (_isReleasePending)
                return;

            _isReleasePending = true;
            StartCoroutine(ReleaseNextFrame());
        }

        private IEnumerator ReleaseNextFrame()
        {
            yield return null;
            _isReleasePending = false;

            // Not let go meanwhile, and not taken properly since (_isHeld).
            if (!_isHeld && _handle.IsHeld)
                _handle.ForceRelease();
        }

        private void OnReleased(VrGrabArgs args)
        {
            EndHold();
        }

        /// <summary>
        /// The hand let go (or the mouthpiece slipped out): the links swing back from where they are now.
        /// </summary>
        private void EndHold()
        {
            if (!_isHeld)
                return;

            _isHeld = false;
            _hand = null;
            StopHeroUse();
            _handleTrigger.transform.localPosition = Vector3.zero;
            for (var i = 0; i < _links.Length; i++)
            {
                _releasedRotations[i] = _links[i].localRotation;
                _writtenRotations[i] = _releasedRotations[i];
            }

            _swingBack = 0f;
            enabled = true;
            Logger.Log($"[VRWaterPipe] {_logName}: mouthpiece let go - the hose swings back.", LogCat.VR);
        }

        /// <summary>
        /// VRMouth isn't the only way to the mouthpiece: a hand closing right at it may hit the pipe's own collider
        /// first (the near grab takes whichever it finds). Then the pipe isn't used - the mouthpiece is put into that
        /// hand a frame later instead (not inside the backend's grab event, architecture.md "Inside Gothic-VR" 4).
        /// true: taken over, the mob's grab does nothing else.
        /// </summary>
        public bool TryTakeOverGrab(VrGrabArgs args)
        {
            if (args.Kind != VrGrabberKind.Hand || _handle == null || _isHeld || _isTakeOverPending)
                return false;

            var hand = _vrPlayerService.Rig?.GetHandVisual(args.HandSide);
            if (hand == null ||
                (hand.position - _mouthpiece.position).sqrMagnitude > _takeOverDistance * _takeOverDistance)
                return false;

            _isBusy = IsBusy();
            if (_isBusy)
                return false;

            _isTakeOverPending = true;
            StartCoroutine(TakeOverGrab(args.HandSide));
            return true;
        }

        private IEnumerator TakeOverGrab(VrHandSide side)
        {
            yield return null;
            _isTakeOverPending = false;

            var rig = _vrPlayerService.Rig;
            if (rig == null || !rig.TryGrabIntoHand(side, _handle))
                Logger.LogWarning($"[VRWaterPipe] {_logName}: the mouthpiece couldn't be put into the {side} hand.",
                    LogCat.VR);
        }

        private void OnDisable()
        {
            // Culled with its mob while waiting: the next grab must still work.
            _isTakeOverPending = false;
            _isReleasePending = false;
            // Culled at the mouth: the mouth's trigger may not report it leaving.
            StopHeroUse();
        }

        public string ExhaleAnimation => _exhaleAnimationName;

        /// <summary>
        /// The held mouthpiece at the mouth: the pipe works for the hero like for a smoking NPC (S0 -> S1 and its
        /// loop: it bubbles, the bowl smokes).
        /// </summary>
        public bool TryStartSmoking(Collider part)
        {
            if (part != _handleTrigger || !_isHeld)
                return false;

            if (_isAtMouth)
                return true;

            _isAtMouth = true;
            _mobAnimationService.StartHeroUse(_mob, _playerService.HeroContainer);
            Logger.Log($"[VRWaterPipe] {_logName}: smoked - it bubbles.", LogCat.VR);
            return true;
        }

        public void StopSmoking(Collider part)
        {
            if (part == _handleTrigger)
                StopHeroUse();
        }

        /// <summary>
        /// Away from the mouth, let go or culled: the pipe rests again (T_S1_2_S0). Only this pipe - not another mob
        /// the hero uses meanwhile.
        /// </summary>
        private void StopHeroUse()
        {
            if (!_isAtMouth)
                return;

            _isAtMouth = false;
            _mobAnimationService.StopHeroUse(_mob);
            Logger.Log($"[VRWaterPipe] {_logName}: away from the mouth - it rests.", LogCat.VR);
        }

        private void LateUpdate()
        {
            if (_isHeld)
                UpdateHeld();
            else
                UpdateSwingBack();
        }

        /// <summary>
        /// Late: after MobAnimator (Update) - the hand wins over the pipe's own animation. Starts from the last solved
        /// pose, so the hose bends smoothly over the frames.
        /// </summary>
        private void UpdateHeld()
        {
            if (_hand == null)
            {
                Slip("the hand is gone");
                return;
            }

            var target = _hand.TransformPoint(_grabOffset);
            if ((target - _links[0].position).sqrMagnitude > _maxReachSqr)
            {
                Slip("pulled further than the hose is long");
                return;
            }

            for (var i = 0; i < _links.Length; i++)
                _links[i].SetLocalPositionAndRotation(_restPositions[i], _solvedRotations[i]);
            _mouthpiece.SetLocalPositionAndRotation(_mouthpieceRestPosition, _mouthpieceRestRotation);

            HoseIk.Solve(_links, _mouthpiece, target, _ikIterations, _ikMaxTurnDegrees, _ikTolerance);

            for (var i = 0; i < _links.Length; i++)
                _solvedRotations[i] = _links[i].localRotation;

            // The mouth meets the mouthpiece where the hand holds it, even when the hose falls a bit short of the hand
            // (the trigger stayed at the hose's end and never reached the mouth).
            _handleTrigger.transform.position = target;
        }

        private void Slip(string reason)
        {
            Logger.Log($"[VRWaterPipe] {_logName}: the mouthpiece slips out of the hand ({reason}).", LogCat.VR);
            _handle.ForceRelease();
            // The backend didn't report a release (not held any more): end it here.
            EndHold();
        }

        /// <summary>
        /// The links turn back within _swingBackSeconds - to their rest pose, or, while the pipe's own animation moves
        /// them (MobAnimator wrote another rotation since the last frame), to that pose.
        /// </summary>
        private void UpdateSwingBack()
        {
            _swingBack = Mathf.Min(1f, _swingBack + Time.deltaTime / _swingBackSeconds);
            var blend = Mathf.SmoothStep(0f, 1f, _swingBack);
            for (var i = 0; i < _links.Length; i++)
            {
                var link = _links[i];
                var current = link.localRotation;
                var goal = current == _writtenRotations[i] ? _restRotations[i] : current;
                var rotation = Quaternion.Slerp(_releasedRotations[i], goal, blend);
                link.localRotation = rotation;
                _writtenRotations[i] = rotation;
            }

            if (_swingBack >= 1f)
                enabled = false;
        }
    }
}
