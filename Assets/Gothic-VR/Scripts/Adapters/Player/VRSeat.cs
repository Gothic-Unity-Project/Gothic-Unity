#if GUZ_HVR_INSTALLED
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GUZ.Core;
using GUZ.Core.Extensions;
using GUZ.Core.Services.Context;
using HurricaneVR.Framework.Core;
using HurricaneVR.Framework.Core.Grabbers;
using HurricaneVR.Framework.Core.Player;
using HurricaneVR.Framework.Core.Utils;
using Reflex.Attributes;
using UnityEngine;

namespace GUZ.VR.Adapters.Player
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
        [SerializeField] private HVRCanvasFade _canvasFade;
        [SerializeField] private float _fadeSpeed = 6f;


        private bool _isPlayerSeated;

        //when NPC sits down this should be changed to true and when NPC stands up change it back to false
        private bool _isNpcSeated;

        private List<Transform> _snapPoints = new();
        private Transform _currentSnapPoint;

        private bool _canPlayerSit = true;

        private void GetSnapPoints()
        {
            var zsPosParent = transform.FindChildRecursive("ZS_POS0").parent;
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

        public void OnGrabbed(HVRGrabberBase grabber, HVRGrabbable grabbable)
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
            
            _canvasFade = player.FindChildRecursively("GlobalCameraFade").GetComponent<HVRCanvasFade>();

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
            _canvasFade.Fade(1f, _fadeSpeed);
            yield return new WaitUntil(() => _canvasFade.CurrentFade >= 1f);

            // Move player
            player.transform.position = _currentSnapPoint.position + _currentSnapPoint.TransformDirection(_posOffset);
            player.transform.eulerAngles = _currentSnapPoint.eulerAngles + _eulerOffset;

            // Fade out
            _canvasFade.Fade(0f, _fadeSpeed);
            yield return new WaitUntil(() => _canvasFade.CurrentFade <= 0f);
        }

        private IEnumerator StandUp(GameObject player)
        {
            // Fade in
            _canvasFade.Fade(1f, _fadeSpeed);
            yield return new WaitUntil(() => _canvasFade.CurrentFade >= 1f);

            // Move player
            player.transform.position += player.transform.TransformDirection(new Vector3(0, 0.5f, 1f));
            _contextInteractionService.UnlockPlayer();

            // Fade out
            _canvasFade.Fade(0f, _fadeSpeed);
            yield return new WaitUntil(() => _canvasFade.CurrentFade <= 0f);
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
#endif
