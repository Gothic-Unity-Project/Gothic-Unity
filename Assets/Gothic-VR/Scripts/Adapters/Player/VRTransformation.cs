using System.Collections;
using Gothic.Core;
using Gothic.Core.Models.Npc;
using Gothic.Core.Services.Npc;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Reflex.Attributes;
using UnityEngine;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// The transformed hero (TransformationService): the player walks and runs like the monster and looks around from its
    /// eye height; the trigger turns him back, like Gothic's action key.
    /// </summary>
    public class VRTransformation : MonoBehaviour
    {
        private const float _fadeSpeed = 4f;

        [Inject] private readonly IVrInput _vrInput;
        [Inject] private readonly TransformationService _transformationService;

        private IVrRig _rig;
        private float _humanMoveSpeed;
        private float _humanRunSpeed;
        private bool _isTransformed;

        private void Start()
        {
            _rig = GetComponent<IVrRig>();
            GlobalEventDispatcher.HeroTransformed.AddListener(OnTransformed);
            GlobalEventDispatcher.HeroTransformedBack.AddListener(OnTransformedBack);
        }

        private void OnDestroy()
        {
            GlobalEventDispatcher.HeroTransformed.RemoveListener(OnTransformed);
            GlobalEventDispatcher.HeroTransformedBack.RemoveListener(OnTransformedBack);
        }

        private void Update()
        {
            if (!_isTransformed)
                return;

            if (_vrInput.GetButton(VrHandSide.Left, VrButton.Trigger).IsJustActivated ||
                _vrInput.GetButton(VrHandSide.Right, VrButton.Trigger).IsJustActivated)
                _transformationService.TransformBack();
        }

        private void OnTransformed(HeroTransformation transformation)
        {
            if (_rig == null)
                return;

            _humanMoveSpeed = _rig.MoveSpeed;
            _humanRunSpeed = _rig.RunSpeed;
            _rig.MoveSpeed = _humanMoveSpeed * transformation.WalkSpeedFactor;
            _rig.RunSpeed = _humanRunSpeed * transformation.RunSpeedFactor;
            _isTransformed = true;
            StartCoroutine(ChangeEyeHeight(transformation.EyeHeight));
        }

        private void OnTransformedBack()
        {
            if (_rig == null || !_isTransformed)
                return;

            _rig.MoveSpeed = _humanMoveSpeed;
            _rig.RunSpeed = _humanRunSpeed;
            _isTransformed = false;
            StartCoroutine(ChangeEyeHeight(null));
        }

        /// <summary>
        /// The camera jumps to the new height - behind a short fade.
        /// </summary>
        private IEnumerator ChangeEyeHeight(float? meters)
        {
            _rig.Fade(1f, _fadeSpeed);
            yield return new WaitUntil(() => _rig.FadeAlpha >= 1f);
            _rig.SetEyeHeightOverride(meters);
            yield return null;
            _rig.Fade(0f, _fadeSpeed);
        }
    }
}
