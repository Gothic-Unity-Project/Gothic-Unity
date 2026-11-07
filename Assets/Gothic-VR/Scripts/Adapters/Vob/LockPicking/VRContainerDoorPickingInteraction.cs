using System.Collections;
using Gothic.Core;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Vm;
using Gothic.Core.Services.Vobs;
using Gothic.VR.Services;
using Gothic.VR.Adapters.Vob.VobItem;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Vob.LockPicking
{
    public class VRContainerDoorPickingInteraction : MonoBehaviour
    {
        [SerializeField] private GameObject _rootGO;
        [SerializeField] private AudioSource _audioSource;

        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly VmService _vmService;
        [Inject] private readonly VrHapticsService _hapticsService;
        [Inject] private readonly HeroPerceptionService _heroPerceptionService;
        [Inject] private readonly MobLockService _mobLockService;
        [Inject] private readonly VrLockTouchService _lockTouchService;

        private const float _lockedCommentCooldown = 8f;
        private float _nextLockedCommentTime;

        private bool _isInitialized;
        private bool _isLocked;
        private string _combination;
        private string _keyInstance;
        private VrHandSide _handSide;
        private VobContainer _lockPick;
        private VobContainer _lockable;
        private IVrHinge _hinge;

        private const string _lockInteractionColliderName = "LockPickInteraction";

        private int _combinationPos = 0;

        public enum DoorLockStatus
        {
            StepSuccess,
            StepFailure,
            Unlocked
        }


        private void Start()
        {
            var vobLoader = GetComponentInParent<VobLoader>();
            _lockable = vobLoader.Container;
            // Every door and chest has one hinge (the door wing or the chest lid) per backend which supports it.
            _hinge = VrHinges.FindActive(vobLoader);
            if (!_lockable.Vob.TryGetLock(out _isLocked, out _keyInstance, out _combination))
                Logger.LogError($"VRDoorLockInteraction: No door or container found for >{_lockable.Vob.Name}<.",
                    LogCat.VR);

            // Stop this handler if the object is already unlocked.
            if (!_isLocked)
            {
                gameObject.SetActive(false);
                return;
            }

            _isInitialized = true;
            RegisterTouch();
            StartCoroutine(StartDelayed());
        }

        private void OnEnable()
        {
            // The first OnEnable runs before Start, which registers then.
            if (_isInitialized)
                RegisterTouch();
        }

        private void OnDisable()
        {
            if (_isInitialized && _lockable?.Go != null)
                _lockTouchService.Unregister(_lockable.Go.transform);
        }

        private void RegisterTouch()
        {
            if (_lockable?.Go != null)
                _lockTouchService.Register(_lockable.Go.transform, this);
        }

        private IEnumerator StartDelayed()
        {
            yield return null;

            // Deactivate rotation
            _hinge?.Lock();
        }

        /// <summary>
        /// An empty hand touched the locked door/chest (VrLockTouchService), like the engine trying it without key or
        /// lock pick: the key in the inventory opens it, else the hero says why it doesn't open ($KEYMISSING, ...).
        /// </summary>
        public void OnEmptyHandTouched(VrHandSide side)
        {
            if (_mobLockService.CanHeroOpenWithKey(_lockable.Vob))
            {
                PlaySound(_vmService.PickLockSuccessSoundName, _vmService.DoorLockSoundName);
                GlobalEventDispatcher.LockPickComboFinished.Invoke(null, _lockable, (int)side);
                UnlockAndPushOpen();
                return;
            }

            if (Time.time < _nextLockedCommentTime)
                return;

            _nextLockedCommentTime = Time.time + _lockedCommentCooldown;
            _hapticsService.Vibrate(side, VrHapticPreset.Info);
            _mobLockService.HeroSayLocked(_lockable.Vob);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (!other.gameObject.name.Equals(_lockInteractionColliderName))
            {
                TryUnlockWithKey(other);
                return;
            }

            // Only a lock pick held in a hand picks the lock - one lying around (e.g. in a chest next to the door)
            // has no hand to track and threw every frame.
            Transform holdingHand;
            if (IsHeldItem(_vrPlayerService.GrabbedItemLeft, other.gameObject))
            {
                holdingHand = _vrPlayerService.GrabbedItemLeft.transform;
                _handSide = VrHandSide.Left;
            }
            else if (IsHeldItem(_vrPlayerService.GrabbedItemRight, other.gameObject))
            {
                holdingHand = _vrPlayerService.GrabbedItemRight.transform;
                _handSide = VrHandSide.Right;
            }
            else
            {
                return;
            }

            // The hand turns, not the pick's world angle.
            var isHandRollTracked = false;
            var handVisual = _vrPlayerService.VRContextInteractionService?.Rig?.GetHandVisual(_handSide);
            if (handVisual != null)
            {
                holdingHand = handVisual;
                isHandRollTracked = true;
            }

            Logger.Log($"[Lock] Lock pick in the lock of '{_lockable.Vob.Name}' ({_handSide} hand, combination " +
                       $"{_combination?.Length ?? 0} steps)", LogCat.VR);
            _combinationPos = 0;
            PlaySound(_vmService.DoorLockSoundName);
            // Like OpenGothic: trying a locked chest/door is using it - its owner may see it.
            _heroPerceptionService.OnHeroUsesMob(_lockable.Vob as IInteractiveObject);
            _hapticsService.Vibrate(_handSide, VrHapticPreset.Info);

            // For later event usage.
            _lockPick = other.gameObject.GetComponentInParent<VobLoader>().Container;

            var lockPickProperties = other.gameObject.GetComponentInParent<VRLockPickProperties>();
            lockPickProperties.IsInsideLock = true;
            lockPickProperties.ActiveContainerDoorPicking = this;
            lockPickProperties.HoldingHand = holdingHand;
            lockPickProperties.IsHandRollTracked = isHandRollTracked;
        }

        private static bool IsHeldItem(GameObject heldItem, GameObject lockInteraction)
        {
            if (heldItem == null)
                return false;
            var interaction = heldItem.GetComponentInChildren<VRLockPickInteraction>();
            return interaction != null && interaction.gameObject == lockInteraction;
        }

        private void OnTriggerExit(Collider other)
        {
            if (!other.gameObject.name.Equals(_lockInteractionColliderName))
            {
                return;
            }

            var lockPickProperties = other.gameObject.GetComponentInParent<VRLockPickProperties>();
            if (lockPickProperties.IsInsideLock)
                Logger.Log($"[Lock] Lock pick out of the lock of '{_lockable.Vob.Name}'", LogCat.VR);
            lockPickProperties.IsInsideLock = false;
            lockPickProperties.ActiveContainerDoorPicking = null;
            lockPickProperties.HoldingHand = null;
        }

        public DoorLockStatus UpdateCombination(bool isLeft)
        {
            var currentChar = _combination[_combinationPos];
            var isCorrect = (isLeft && currentChar == 'L') || (!isLeft && currentChar == 'R');

            Logger.Log($"IsCorrect={isCorrect}, CombinationChar={currentChar}", LogCat.VR);

            if (isCorrect)
            {
                _combinationPos++;

                // Just a correct step, but not yet finished.
                if (_combinationPos != _combination.Length)
                {
                    GlobalEventDispatcher.LockPickComboCorrect.Invoke(_lockPick, _lockable, (int)_handSide);

                    PlaySound(_vmService.PickLockSuccessSoundName);
                    return DoorLockStatus.StepSuccess;
                }
                // Unlocked!
                else
                {
                    GlobalEventDispatcher.LockPickComboFinished.Invoke(_lockPick, _lockable, (int)_handSide);

                    UnlockAndPushOpen();

                    return DoorLockStatus.Unlocked;
                }
            }
            else
            {
                // FIXME - Pseudo breaking for testings only. Use real skill value from hero.
                if (Random.value > 0.5f)
                {
                    GlobalEventDispatcher.LockPickComboWrong.Invoke(_lockPick, _lockable, (int)_handSide);
                    PlaySound(_vmService.PickLockFailureSoundName);
                }
                else
                {
                    GlobalEventDispatcher.LockPickComboBroken.Invoke(_lockPick, _lockable, (int)_handSide);
                    PlaySound(_vmService.PickLockBrokenSoundName);
                }

                _combinationPos = 0;
                return DoorLockStatus.StepFailure;
            }
        }

        /// <summary>
        /// Like the engine: the door's/chest's key (held in a hand) opens it without picking the lock.
        /// </summary>
        private void TryUnlockWithKey(Collider other)
        {
            if (string.IsNullOrEmpty(_keyInstance))
                return;

            // Any VOB can enter the trigger (not only items) - VobAs<IItem>() would throw an InvalidCastException.
            var vobLoader = other.gameObject.GetComponentInParent<VobLoader>();
            if (vobLoader?.Container?.Vob is not IItem vobItem)
                return;

            var itemInstance = vobItem.GetSymbolName();
            if (!itemInstance.EqualsIgnoreCase(_keyInstance))
                return;

            // The player must actively hold the key (not just a dropped/flying item).
            VrHandSide hand;
            var leftRoot = _vrPlayerService.GrabbedItemLeft;
            var rightRoot = _vrPlayerService.GrabbedItemRight;
            if (leftRoot != null && leftRoot.GetComponentInParent<VobLoader>() == vobLoader)
                hand = VrHandSide.Left;
            else if (rightRoot != null && rightRoot.GetComponentInParent<VobLoader>() == vobLoader)
                hand = VrHandSide.Right;
            else
                return;

            Logger.Log($"[Lock] Key unlock: '{_keyInstance}' matched.", LogCat.VR);
            // LockPickComboFinished also gives the success vibration (VrHapticsService).
            PlaySound(_vmService.PickLockSuccessSoundName, _vmService.DoorLockSoundName);
            GlobalEventDispatcher.LockPickComboFinished.Invoke(vobLoader.Container, _lockable, (int)hand);
            UnlockAndPushOpen();
        }

        private void UnlockAndPushOpen()
        {
            // Reactivate rotation
            if (_hinge == null)
            {
                gameObject.SetActive(false);
                return;
            }

            _hinge.Unlock();

            // Physics hinges (HVR) get pushed open here, kinematic ones (VrHinge) push themselves in Unlock().
            var body = _hinge.Rigidbody;
            if (body != null && !body.isKinematic)
            {
                var pushDirection = _hinge.Transform.forward;
                var pushForce = 10f;
                body.AddForce(pushDirection * pushForce, ForceMode.Impulse);
            }

            gameObject.SetActive(false);
        }

        private void PlaySound(string soundName, string fallback = null)
        {
            // G2 has no _STR_SOUND_PICKLOCK_* symbols (null) - the fallback plays.
            var clip = string.IsNullOrEmpty(soundName) ? null : _audioService.GetRandomSoundClip(soundName);

            if (clip == null && fallback != null)
            {
                PlaySound(fallback);
                return;
            }

            if (clip == null)
                return;

            _audioSource.PlayOneShot(clip);
        }
    }
}
