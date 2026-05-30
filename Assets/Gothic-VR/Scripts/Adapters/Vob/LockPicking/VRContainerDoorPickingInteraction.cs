#if GUZ_HVR_INSTALLED
using System.Collections;
using GUZ.Core;
using GUZ.Core.Adapters.Vob;
using GUZ.Core.Logging;
using GUZ.Core.Manager;
using GUZ.Core.Models.Container;
using GUZ.Core.Services.Vm;
using GUZ.VR.Adapters.Vob.VobItem;
using GUZ.VR.Services;
using HurricaneVR.Framework.Components;
using HurricaneVR.Framework.Shared;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Vobs;
using Logger = GUZ.Core.Logging.Logger;

namespace GUZ.VR.Adapters.Vob.LockPicking
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
                    break;
                case IContainer container:
                    _isLocked = container.IsLocked;
                    _combination = container.PickString;
                    break;
                default:
                    Logger.LogError($"VRDoorLockInteraction: No door or container found for >{_lockable.Vob.Name}<.", LogCat.VR);
                    break;
            }

            // Stop this handler if the object is already unlocked.
            if (!_isLocked)
                gameObject.SetActive(false);

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
                return;

            // FIXME - Check if LockPick is in any hand. Otherwise its "flying" around and needs to be ignored.

            _combinationPos = 0;
            PlaySound(_vmService.DoorLockSoundName);

            // For later event usage.
            _lockPick = other.gameObject.GetComponentInParent<VobLoader>().Container;

            var lockPickProperties = other.gameObject.GetComponentInParent<VRLockPickProperties>();
            lockPickProperties.IsInsideLock = true;
            lockPickProperties.ActiveContainerDoorPicking = this;


            if (_vrPlayerService.GrabbedItemLeft?.GetComponentInChildren<VRLockPickInteraction>().gameObject == other.gameObject)
            {
                lockPickProperties.HoldingHand = _vrPlayerService.GrabbedItemLeft!.transform;
                _handSide = HVRHandSide.Left;
                _hapticsService.Vibrate(HVRHandSide.Left, VrHapticsService.VibrationType.Info);
            }
            else if (_vrPlayerService.GrabbedItemRight?.GetComponentInChildren<VRLockPickInteraction>().gameObject == other.gameObject)
            {
                lockPickProperties.HoldingHand = _vrPlayerService.GrabbedItemRight!.transform;
                _handSide = HVRHandSide.Right;
                _hapticsService.Vibrate(HVRHandSide.Right, VrHapticsService.VibrationType.Info);
            }
            else
            {
                Logger.LogError($"VRDoorLockInteraction: No hand found for grabbed object >{other.gameObject.name}<.", LogCat.VR);
            }
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

        private void PlaySound(string soundName, string fallback = null)
        {
            var clip = _audioService.GetRandomSoundClip(soundName);
            
            if (clip == null && fallback != null)
            {
                PlaySound(fallback);
                return;
            }
                
            _audioSource.PlayOneShot(clip);
        }
    }
}
#endif

