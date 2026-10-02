#if GOTHIC_HVR_INSTALLED
using System.Collections;
using Gothic.Core;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Vm;
using Gothic.VR.Services;
using Gothic.VR.Adapters.Vob.VobItem;
using HurricaneVR.Framework.Components;
using HurricaneVR.Framework.Shared;
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
        [SerializeField] private HVRPhysicsDoor _hvrPhysicsDoor;

        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly VmService _vmService;
        [Inject] private readonly VrHapticsService _hapticsService;

        private bool _isLocked;
        private string _combination;
        private string _keyInstance;
        private HVRHandSide _handSide;
        private VobContainer _lockPick;
        private VobContainer _lockable;

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
            _lockable = GetComponentInParent<VobLoader>().Container;
            switch (_lockable.Vob)
            {
                case IDoor door:
                    _isLocked = door.IsLocked;
                    _combination = door.PickString;
                    _keyInstance = door.Key;
                    break;
                case IContainer container:
                    _isLocked = container.IsLocked;
                    _combination = container.PickString;
                    _keyInstance = container.Key;
                    break;
                default:
                    Logger.LogError($"VRDoorLockInteraction: No door or container found for >{_lockable.Vob.Name}<.", LogCat.VR);
                    break;
            }

            // Stop this handler if the object is already unlocked.
            if (!_isLocked)
            {
                gameObject.SetActive(false);
                return;
            }

            StartCoroutine(StartDelayed());
        }

        private IEnumerator StartDelayed()
        {
            yield return null;

            // Deactivate rotation
            _hvrPhysicsDoor.Lock();
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
                _handSide = HVRHandSide.Left;
            }
            else if (IsHeldItem(_vrPlayerService.GrabbedItemRight, other.gameObject))
            {
                holdingHand = _vrPlayerService.GrabbedItemRight.transform;
                _handSide = HVRHandSide.Right;
            }
            else
            {
                return;
            }

            _combinationPos = 0;
            PlaySound(_vmService.DoorLockSoundName);
            _hapticsService.Vibrate(_handSide, VrHapticsService.VibrationType.Info);

            // For later event usage.
            _lockPick = other.gameObject.GetComponentInParent<VobLoader>().Container;

            var lockPickProperties = other.gameObject.GetComponentInParent<VRLockPickProperties>();
            lockPickProperties.IsInsideLock = true;
            lockPickProperties.ActiveContainerDoorPicking = this;
            lockPickProperties.HoldingHand = holdingHand;
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

                    // Reactivate rotation
                    _hvrPhysicsDoor.Unlock();

                    // Get the door's forward direction in world space
                    var pushDirection = _hvrPhysicsDoor.transform.forward;
                    var pushForce = 10f;

                    // Apply force in the door's forward direction
                    _hvrPhysicsDoor.GetComponent<Rigidbody>().AddForce(pushDirection * pushForce, ForceMode.Impulse);
                    gameObject.SetActive(false);

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

        private void TryUnlockWithKey(Collider other)
        {
            if (string.IsNullOrEmpty(_keyInstance))
                return;

            var vobLoader = other.gameObject.GetComponentInParent<VobLoader>();
            if (vobLoader == null || vobLoader.Container == null)
                return;

            // Any VOB can enter the trigger (not only items) - VobAs<IItem>() would throw an InvalidCastException.
            if (vobLoader.Container.Vob is not IItem vobItem)
                return;

            var itemInstance = !string.IsNullOrEmpty(vobItem.Instance) ? vobItem.Instance : vobItem.Name;
            if (!itemInstance.EqualsIgnoreCase(_keyInstance))
                return;

            // Confirm the player is actively holding the key (not just a dropped/flying item).
            HVRHandSide hand;
            var leftRoot = _vrPlayerService.GrabbedItemLeft;
            var rightRoot = _vrPlayerService.GrabbedItemRight;
            if (leftRoot != null && leftRoot.GetComponentInParent<VobLoader>() == vobLoader)
                hand = HVRHandSide.Left;
            else if (rightRoot != null && rightRoot.GetComponentInParent<VobLoader>() == vobLoader)
                hand = HVRHandSide.Right;
            else
                return;

            Logger.Log($"[Lock] Key unlock: '{_keyInstance}' matched.", LogCat.VR);
            _hapticsService.Vibrate(hand, VrHapticsService.VibrationType.Success);
            PlaySound("PICKLOCK_UNLOCK", "DOOR_LOCK.WAV");
            GlobalEventDispatcher.LockPickComboFinished.Invoke(vobLoader.Container, _lockable, (int)hand);
            _hvrPhysicsDoor.Unlock();
            _hvrPhysicsDoor.GetComponent<Rigidbody>().AddForce(_hvrPhysicsDoor.transform.forward * 10f, ForceMode.Impulse);
            gameObject.SetActive(false);
        }

        private void PlaySound(string soundName, string fallback = null)
        {
            var clip = _audioService.GetRandomSoundClip(soundName);

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
#endif
