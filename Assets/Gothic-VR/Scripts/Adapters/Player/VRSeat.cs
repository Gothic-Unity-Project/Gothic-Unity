using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Services.Context;
using Gothic.Core;
using Gothic.Core.Extensions;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Services.Context;
using Reflex.Attributes;
using UnityEngine;

namespace Gothic.VR.Adapters.Player
{
    public class VRSeat : MonoBehaviour
    {
        [Inject] private readonly ContextInteractionService _contextInteractionService;
        
        //this will enable player to sit on benches/chairs etc
        private Vector3 _posOffset = new(0, -0.75f, 0.9f);
        private Vector3 _eulerOffset = new(0, 180, 0);
        private const float _sittingCooldown = 0.5f;
        
        // Fade components
        [Header("Vignette Settings")]
        [SerializeField] private float _fadeSpeed = 6f;


        private IVrRig _rig;
        private bool _isPlayerSeated;

        //when NPC sits down this should be changed to true and when NPC stands up change it back to false
        private bool _isNpcSeated;

        private List<Transform> _snapPoints = new();
        private Transform _currentSnapPoint;

        private bool _canPlayerSit = true;
        private VrInteractable _interactable;

        private void Awake()
        {
            _interactable = VrInteractable.Find(this);
            _interactable.Grabbed += OnGrabbed;
        }

        private void OnDestroy()
        {
            if (_interactable != null)
                _interactable.Grabbed -= OnGrabbed;
        }

        private void GetSnapPoints()
        {
            var zsPosParent = gameObject.FindChildRecursively("ZS_POS0").transform.parent;
            //find children that contain "ZS_POS" in object name and add to list of snap points
            for (var i = 0; i < zsPosParent.childCount; i++)
            {
                var child = zsPosParent.GetChild(i);
                if (child.name.ToLower().Contains("zs_pos"))
                {
                    //add snap point
                    _snapPoints.Add(child);
                }
            }
        }

        private void OnGrabbed(VrGrabArgs args)
        {
            if (_snapPoints.IsEmpty())
            {
                GetSnapPoints();
            }

            if (_isNpcSeated || !_canPlayerSit)
            {
                return; // stop player from sitting if an NPC is already sitting there or if its cooling down
            }

            //get player object
            _canPlayerSit = false; // disable this function to cooldown

            GameObject player = _contextInteractionService.GetCurrentPlayerController();
            
            _rig = _contextInteractionService.GetImpl<VRContextInteractionService>().Rig;

            //handle sitting/standing
            _isPlayerSeated = !_isPlayerSeated;
            if (_isPlayerSeated)
            {
                StartCoroutine(SitDown(player));
            }
            else
            {
                StartCoroutine(StandUp(player));
            }

            Invoke(nameof(EnableSitting), _sittingCooldown);
        }

        private void EnableSitting()
        {
            _canPlayerSit = true;
        }

        private IEnumerator SitDown(GameObject player)
        {
            _contextInteractionService.LockPlayerInPlace();
            _currentSnapPoint = GetNearestSnapPoint(player.transform.position);

            // Fade in
            _rig.Fade(1f, _fadeSpeed);
            yield return new WaitUntil(() => _rig.FadeAlpha >= 1f);

            // Move player
            player.transform.position = _currentSnapPoint.position + _currentSnapPoint.TransformDirection(_posOffset);
            player.transform.eulerAngles = _currentSnapPoint.eulerAngles + _eulerOffset;

            // Fade out
            _rig.Fade(0f, _fadeSpeed);
            yield return new WaitUntil(() => _rig.FadeAlpha <= 0f);
        }

        private IEnumerator StandUp(GameObject player)
        {
            // Fade in
            _rig.Fade(1f, _fadeSpeed);
            yield return new WaitUntil(() => _rig.FadeAlpha >= 1f);

            // Move player
            player.transform.position += player.transform.TransformDirection(new Vector3(0, 0.5f, 1f));
            _contextInteractionService.UnlockPlayer();

            // Fade out
            _rig.Fade(0f, _fadeSpeed);
            yield return new WaitUntil(() => _rig.FadeAlpha <= 0f);
        }

        private Transform GetNearestSnapPoint(Vector3 pos)
        {
            // Find the closest Snap Point for player to sit.
            return _snapPoints
                .OrderBy(point => Vector3.Distance(pos, point.position))
                .First();
        }
    }
}
