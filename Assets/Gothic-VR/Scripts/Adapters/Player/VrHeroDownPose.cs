using System;
using System.Collections.Generic;
using Gothic.Core;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Npc;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Models;
using Gothic.VR.Services;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;
using Object = UnityEngine.Object;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// Accessibility option heroFallMode "move with the body" (VRPlayerService.HeroFallMode, the default): knocked out
    /// (HeroKnockedOut) or dead (HeroDied) the body falls on its back (T_STAND_2_WOUNDEDB, S_WOUNDEDB / T_DEADB,
    /// S_DEADB) and stands up again (HeroRecovered: T_WOUNDEDB_2_STAND); put to sleep (HeroFellAsleep) it lies down
    /// like ZS_MagicSleep (T_STAND_2_VICTIM_SLE, S_VICTIM_SLE) and gets up on HeroWokeUp (T_VICTIM_SLE_2_STAND).
    /// The camera follows the animated head down and up - only its height (IVrRig.CameraYOffset,
    /// like a transformation); the body lies out from under it, so the view never moves sideways and where you look
    /// stays yours. It only follows the hero's events: FightService decides death and the locks, VRPlayer opens the
    /// death menu. Tells VRPlayerService whether a body that can fall exists (SetHeroFallBody); "blink" leaves the
    /// view to VrKnockoutView.
    /// Worker of VRHeroBody (new().Inject(), Initialize, Bind, Dispose).
    /// </summary>
    public class VrHeroDownPose
    {
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly NpcService _npcService;

        private enum DownPhase
        {
            None,
            Knockout,
            Asleep,
            Dead,
            Rising
        }

        private const string _standToWounded = "T_STAND_2_WOUNDEDB";
        private const string _wounded = "S_WOUNDEDB";
        private const string _standToDead = "T_DEADB";
        private const string _dead = "S_DEADB";
        private const string _woundedToDead = "T_WOUNDEDB_2_DEADB";
        private const string _woundedToStand = "T_WOUNDEDB_2_STAND";
        // ZS_MagicSleep's animations.
        private const string _standToSleep = "T_STAND_2_VICTIM_SLE";
        private const string _sleep = "S_VICTIM_SLE";
        private const string _sleepToStand = "T_VICTIM_SLE_2_STAND";

        private const float _downFloorMargin = 0.03f;
        private const float _minDownEyeHeight = 0.2f;
        // The neck sits this far below the eyes (scaled with the body).
        private readonly float _neckBelowEyes;
        private readonly Action _solveArms;

        private DownPhase _downPhase;
        // The hero's state from the events (HeroKnockedOut, HeroRecovered, HeroDied, HeroFellAsleep, HeroWokeUp,
        // LoadGameStart).
        private bool _isKnockedOut;
        private bool _isDead;
        private bool _isAsleep;
        // The hands' physics before the hero went down.
        private readonly Dictionary<VrPhysicsHand, bool> _physicalBeforeDown = new();
        private readonly Queue<string> _downAnimations = new();
        private string _downAnimation;
        private Vector3 _downForward;
        private float _downBaseCameraOffset;
        private float _downStartEyeHeight = -1f;
        private bool _isMenuOpen;
        private bool _isEnabled = true;
        private Transform[] _bodyBones;

        private GameObject _body;
        private VRHeroBodyAnimator _animator;
        private Transform _headBone;
        private VrHeroArmIk _leftArm;
        private VrHeroArmIk _rightArm;
        private IVrRig _rig;

        /// <param name="neckBelowEyes">The neck sits this far below the eyes (scaled with the body).</param>
        /// <param name="solveArms">The arms reach for the VR hands (not while dead).</param>
        public VrHeroDownPose(float neckBelowEyes, Action solveArms)
        {
            _neckBelowEyes = neckBelowEyes;
            _solveArms = solveArms;
        }

        public void Initialize()
        {
            // Built while he's already down (a body created later): once from his state, then by the events.
            var bodyState = _npcService.GetHeroContainer()?.Props?.BodyState;
            _isDead = bodyState == VmGothicEnums.BodyState.BsDead;
            _isKnockedOut = bodyState == VmGothicEnums.BodyState.BsUnconscious;

            GlobalEventDispatcher.HeroKnockedOut.AddListener(OnHeroKnockedOut);
            GlobalEventDispatcher.HeroRecovered.AddListener(OnHeroRecovered);
            GlobalEventDispatcher.HeroDied.AddListener(OnHeroDied);
            GlobalEventDispatcher.HeroFellAsleep.AddListener(OnHeroFellAsleep);
            GlobalEventDispatcher.HeroWokeUp.AddListener(OnHeroWokeUp);
            GlobalEventDispatcher.LoadGameStart.AddListener(OnLoadGameStart);
            GlobalEventDispatcher.MenuOpened.AddListener(OnMenuOpened);
            GlobalEventDispatcher.MenuClosed.AddListener(OnMenuClosed);
            UpdateFallCameraOwner();
        }

        public void Dispose()
        {
            GlobalEventDispatcher.HeroKnockedOut.RemoveListener(OnHeroKnockedOut);
            GlobalEventDispatcher.HeroRecovered.RemoveListener(OnHeroRecovered);
            GlobalEventDispatcher.HeroDied.RemoveListener(OnHeroDied);
            GlobalEventDispatcher.HeroFellAsleep.RemoveListener(OnHeroFellAsleep);
            GlobalEventDispatcher.HeroWokeUp.RemoveListener(OnHeroWokeUp);
            GlobalEventDispatcher.LoadGameStart.RemoveListener(OnLoadGameStart);
            GlobalEventDispatcher.MenuOpened.RemoveListener(OnMenuOpened);
            GlobalEventDispatcher.MenuClosed.RemoveListener(OnMenuClosed);
            EndDown();
            _body = null;
            _vrPlayerService.SetHeroFallBody(false);
        }

        /// <summary>
        /// The (re)built body - or none (null: it couldn't be built).
        /// </summary>
        public void Bind(GameObject body, VRHeroBodyAnimator animator, Transform headBone, VrHeroArmIk leftArm,
            VrHeroArmIk rightArm)
        {
            _body = body;
            _animator = animator;
            _headBone = headBone;
            _leftArm = leftArm;
            _rightArm = rightArm;
            _bodyBones = null;
            UpdateFallCameraOwner();
        }

        /// <summary>
        /// The body is disabled: up at once, the knockout view lowers the camera itself meanwhile.
        /// </summary>
        public void SetEnabled(bool isEnabled)
        {
            _isEnabled = isEnabled;
            if (!isEnabled)
                EndDown();
            UpdateFallCameraOwner();
        }

        private void UpdateFallCameraOwner()
        {
            _vrPlayerService.SetHeroFallBody(_isEnabled && _body != null && _animator != null && _animator.IsValid &&
                                             _headBone != null);
        }

        private void OnHeroKnockedOut(NpcContainer hero)
        {
            _isKnockedOut = true;
        }

        private void OnHeroRecovered()
        {
            _isKnockedOut = false;
        }

        private void OnHeroDied(NpcContainer killer)
        {
            _isDead = true;
        }

        private void OnHeroFellAsleep(NpcContainer hero)
        {
            _isAsleep = true;
        }

        private void OnHeroWokeUp()
        {
            _isAsleep = false;
        }

        private void OnLoadGameStart()
        {
            // A loaded game: up at once, the loading screen covers it.
            _isKnockedOut = false;
            _isDead = false;
            _isAsleep = false;
            EndDown();
        }

        private void OnMenuOpened(GameObject menu)
        {
            _isMenuOpen = true;
        }

        private void OnMenuClosed()
        {
            _isMenuOpen = false;
        }

        /// <summary>
        /// The body lies, falls or stands up (per frame, it's an animation). True while it drives the body.
        /// </summary>
        public bool UpdateDown(Transform head, float feetY, float standingScale, IVrRig rig)
        {
            _rig = rig;
            var isDown = _isDead || _isKnockedOut || _isAsleep;
            if (!isDown && _downPhase == DownPhase.Dead)
            {
                EndDown();
                return false;
            }
            if (_vrPlayerService.HeroFallMode != VrHeroFallMode.MoveWithBody || _body == null || _animator == null ||
                !_animator.IsValid || _headBone == null || standingScale <= 0f)
            {
                EndDown();
                return false;
            }

            if (_downPhase == DownPhase.None)
            {
                if (!isDown)
                    return false;
                StartDown(head, standingScale, feetY);
            }

            if (_isDead && _downPhase != DownPhase.Dead)
            {
                if (_downPhase == DownPhase.Knockout)
                    SetDownAnimations(_woundedToDead, _dead);
                else
                    SetDownAnimations(_standToDead, _dead);
                _downPhase = DownPhase.Dead;
            }
            // Knocked out while asleep (the hit woke him) or while getting up.
            else if (!_isDead && _isKnockedOut && _downPhase is DownPhase.Rising or DownPhase.Asleep)
            {
                SetDownAnimations(_standToWounded, _wounded);
                _downPhase = DownPhase.Knockout;
            }
            else if (!_isDead && !_isKnockedOut && _isAsleep && _downPhase == DownPhase.Rising)
            {
                SetDownAnimations(_standToSleep, _sleep);
                _downPhase = DownPhase.Asleep;
            }
            else if (!isDown && _downPhase is DownPhase.Knockout or DownPhase.Asleep)
            {
                SetDownAnimations(_downPhase == DownPhase.Asleep ? _sleepToStand : _woundedToStand);
                _downPhase = DownPhase.Rising;
            }

            if (_downAnimation != null && _animator.IsDone(_downAnimation) && _downAnimations.Count > 0)
            {
                _downAnimation = _downAnimations.Dequeue();
                _animator.Play(_downAnimation);
            }
            if (_downPhase == DownPhase.Rising && (_downAnimation == null || _animator.IsDone(_downAnimation)))
            {
                EndDown();
                return false;
            }

            // The pose: the body keeps its size and direction, lies on the floor (lowest bone), the head under the
            // eyes.
            var scale = standingScale;
            var bodyTransform = _body.transform;
            bodyTransform.localScale = Vector3.one * scale;
            bodyTransform.SetPositionAndRotation(new Vector3(head.position.x, feetY, head.position.z),
                Quaternion.LookRotation(_downForward));
            _animator.Update(Time.deltaTime);

            if (_bodyBones == null || _bodyBones.Length == 0 || _bodyBones[0] == null)
                _bodyBones = GetFloorBones();
            var lowestY = float.MaxValue;
            for (var i = 0; i < _bodyBones.Length; i++)
            {
                var bone = _bodyBones[i];
                if (bone != null && bone != bodyTransform)
                    lowestY = Mathf.Min(lowestY, bone.position.y);
            }
            var headPosition = _headBone.position;
            bodyTransform.position += new Vector3(head.position.x - headPosition.x,
                lowestY < float.MaxValue ? feetY + _downFloorMargin * scale - lowestY : 0f,
                head.position.z - headPosition.z);

            // The eyes go with the animated head, relative to where they were when he fell (StartDown).
            var eyeHeight = _headBone.position.y + _neckBelowEyes * scale - feetY;
            if (_downStartEyeHeight < 0f)
                _downStartEyeHeight = eyeHeight;
            if (IsRigAlive())
                _rig.CameraYOffset = _downBaseCameraOffset +
                                     Mathf.Max(eyeHeight, _minDownEyeHeight) - _downStartEyeHeight;

            // Dead, the arms stay in the death pose - reaching for the controllers, the player moved his own corpse.
            if (_downPhase != DownPhase.Dead)
                _solveArms();
            // ...and the corpse goes while the menu is open: lying in front of the low camera, it was in the way.
            SetBodyShown(_downPhase != DownPhase.Dead || !_isMenuOpen);
            return true;
        }

        /// <summary>
        /// Lying on the floor (knocked out, dead, asleep), the hands follow
        /// the controllers without physics - the physical hands bounced on the floor and shook the lying body.
        /// </summary>
        private void SetHandsThroughFloor(bool isDown)
        {
            var hands = _vrPlayerService.VRContextInteractionService?.PhysicsHands;
            if (hands == null)
                return;

            if (isDown)
            {
                for (var i = 0; i < hands.Count; i++)
                {
                    var hand = hands[i];
                    if (hand == null || _physicalBeforeDown.ContainsKey(hand))
                        continue;
                    _physicalBeforeDown[hand] = hand.IsPhysical;
                    hand.IsPhysical = false;
                }
                return;
            }

            foreach (var (hand, wasPhysical) in _physicalBeforeDown)
            {
                if (hand != null)
                    hand.IsPhysical = wasPhysical;
            }
            _physicalBeforeDown.Clear();
        }

        /// <summary>
        /// The bones that put the lying body on the floor (its lowest one).
        /// Not the arms - they follow the controllers, a hand moved down to the floor lifted the whole body and the
        /// camera with it (the floor "pushed" the player up, the view shook).
        /// </summary>
        private Transform[] GetFloorBones()
        {
            var bones = _body.GetComponentsInChildren<Transform>();

            var leftUpper = _leftArm?.Upper;
            var rightUpper = _rightArm?.Upper;
            return Array.FindAll(bones, bone =>
                (leftUpper == null || !bone.IsChildOf(leftUpper)) &&
                (rightUpper == null || !bone.IsChildOf(rightUpper)));
        }

        /// <summary>
        /// Knocked out he falls wounded (T_STAND_2_WOUNDEDB), dead straight from standing (T_DEADB), asleep like
        /// ZS_MagicSleep (T_STAND_2_VICTIM_SLE). The camera's
        /// start height is the standing pose of the frame before: the first frame of a fall can already be low
        /// (T_DEADB), then the camera never went down.
        /// </summary>
        private void StartDown(Transform head, float standingScale, float feetY)
        {
            _downForward = Vector3.ProjectOnPlane(_body.transform.forward, Vector3.up);
            if (_downForward.sqrMagnitude < 0.0001f)
                _downForward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            _downForward = _downForward.sqrMagnitude < 0.0001f ? Vector3.forward : _downForward.normalized;

            _downBaseCameraOffset = IsRigAlive() ? _rig.CameraYOffset : 0f;
            _downStartEyeHeight = _headBone.position.y + _neckBelowEyes * standingScale - feetY;
            _bodyBones = null;
            SetHandsThroughFloor(true);
            _vrPlayerService.IsHeroBodyDown = true;
            if (_isDead)
            {
                _downPhase = DownPhase.Dead;
                SetDownAnimations(_standToDead, _dead);
            }
            else if (_isKnockedOut)
            {
                _downPhase = DownPhase.Knockout;
                SetDownAnimations(_standToWounded, _wounded);
            }
            else
            {
                _downPhase = DownPhase.Asleep;
                SetDownAnimations(_standToSleep, _sleep);
            }
            Logger.Log("[VRHeroBody] Hero down - body falls, camera follows its head", LogCat.VR);
        }

        private void SetDownAnimations(string first, string second = null)
        {
            _downAnimations.Clear();
            if (_animator.HasAnimation(first))
                _downAnimations.Enqueue(first);
            if (second != null && _animator.HasAnimation(second))
                _downAnimations.Enqueue(second);
            _downAnimation = _downAnimations.Count > 0 ? _downAnimations.Dequeue() : null;
            if (_downAnimation != null)
                _animator.Play(_downAnimation);
        }

        /// <summary>
        /// Back to the normal body; the camera height as before the fall.
        /// </summary>
        private void EndDown()
        {
            if (_downPhase == DownPhase.None)
                return;

            _downPhase = DownPhase.None;
            SetBodyShown(true);
            SetHandsThroughFloor(false);
            _vrPlayerService.IsHeroBodyDown = false;
            _downAnimation = null;
            _downAnimations.Clear();
            if (IsRigAlive())
                _rig.CameraYOffset = _downBaseCameraOffset;
            Logger.Log("[VRHeroBody] Hero up again - camera height restored", LogCat.VR);
        }

        private void SetBodyShown(bool isShown)
        {
            if (_body != null && _body.activeSelf != isShown)
                _body.SetActive(isShown);
        }

        private bool IsRigAlive()
        {
            return _rig != null && !(_rig is Object rigObject && rigObject == null);
        }
    }
}
