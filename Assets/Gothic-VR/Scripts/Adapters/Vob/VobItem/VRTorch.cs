using System.Collections;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Services.Meshes;
using Gothic.Core.Services.World;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Services;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.InputSystem;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Vob.VobItem
{
    /// <summary>
    /// A torch (ITEM_TORCH) is lit / put out with the trigger of the hand holding it (R in the simulator) - like the
    /// engine swapping ItLsTorch for ItLsTorchBurning: fire on its tip, a light that moves with it (the world's one
    /// slot for a moving light, shared with the Light spell), the lighting sound. It keeps burning when let go. Added by
    /// VRVobItem on the first grab. Not saved - torches are unlit after a load.
    /// </summary>
    public class VRTorch : MonoBehaviour
    {
        [Inject] private readonly IVrInput _vrInput;
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly MeshService _meshService;
        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly StationaryLightsService _stationaryLightsService;

        // The engine's torch fire; FIRE_SMALL if a mod lacks it.
        private static readonly string[] _firePfxNames = { "TORCH", "FIRE_SMALL" };
        private const string _lightSound = "TORCH_ENLIGHT";
        private const float _lightRange = 7f;
        private const float _lightIntensity = 1.6f;
        private static readonly Color _lightColor = new(1f, 0.7f, 0.4f);
        // Tip of a mesh without a MeshFilter (shouldn't happen): above the grip.
        private const float _fallbackTipHeight = 0.4f;

        private GameObject _fire;
        private StationaryLight _light;

        private bool IsBurning => _fire != null;

        private void Awake()
        {
            this.Inject();
        }

        private void OnDestroy()
        {
            PutOut();
        }

        private void Update()
        {
            var isLeft = _vrPlayerService.GrabbedItemLeft == gameObject;
            var isRight = _vrPlayerService.GrabbedItemRight == gameObject;
            if (!isLeft && !isRight)
                return;

            bool isPressed;
            if (_vrInput.IsSimulator)
                isPressed = Keyboard.current != null && Keyboard.current[Key.R].wasPressedThisFrame;
            else
                isPressed = (isLeft && _vrInput.GetButton(VrHandSide.Left, VrButton.Trigger).IsJustActivated) ||
                            (isRight && _vrInput.GetButton(VrHandSide.Right, VrButton.Trigger).IsJustActivated);
            if (!isPressed)
                return;

            if (IsBurning)
                PutOut();
            else
                Light();
        }

        private void Light()
        {
            var tip = new GameObject("_TorchFire");
            tip.transform.SetParent(transform, false);
            tip.transform.localPosition = GetTipLocalPosition();

            foreach (var pfxName in _firePfxNames)
            {
                if (_meshService.CreateVobPfx(pfxName, parent: tip) != null)
                    break;
            }
            _fire = tip;

            var lightGo = new GameObject("_TorchLight");
            lightGo.transform.SetParent(tip.transform, false);
            _light = lightGo.AddComponent<StationaryLight>();
            _light.Inject();
            _light.Type = LightType.Point;
            _light.Color = _lightColor;
            _light.Range = _lightRange;
            _light.Intensity = _lightIntensity;
            _light.Index = _stationaryLightsService.DynamicLightIndex;
            _light.Init();
            StartCoroutine(DriveLight());

            var clip = _audioService.GetRandomSoundClip(_lightSound);
            if (clip != null)
                AudioSource.PlayClipAtPoint(clip, tip.transform.position);
            Logger.Log($"[VRTorch] {name} lit", LogCat.VR);
        }

        private void PutOut()
        {
            if (_fire == null)
                return;

            Destroy(_fire);
            _fire = null;
            _light = null;
            _stationaryLightsService?.ClearDynamicLight();
            Logger.Log($"[VRTorch] {name} put out", LogCat.VR);
        }

        /// <summary>
        /// The world shader's moving light slot follows the burning tip (like the Light spell in VRSpellFx).
        /// </summary>
        private IEnumerator DriveLight()
        {
            var linearColor = _lightColor.linear;
            while (_light != null)
            {
                _stationaryLightsService.UpdateDynamicLight(_light.transform.position, _lightRange, linearColor);
                _light.Refresh();
                // Immersion setting "Light refresh" (Low/Medium/High).
                yield return new WaitForSeconds(_stationaryLightsService.MovingLightRefreshSeconds);
            }
        }

        /// <summary>
        /// The end of the torch farther from its pivot (the grip) along its longest axis.
        /// </summary>
        private Vector3 GetTipLocalPosition()
        {
            var meshFilter = GetComponentInChildren<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
                return Vector3.up * _fallbackTipHeight;

            var bounds = meshFilter.sharedMesh.bounds;
            var size = bounds.size;
            var axis = size.x >= size.y && size.x >= size.z ? Vector3.right :
                size.y >= size.z ? Vector3.up : Vector3.forward;
            var min = Vector3.Scale(bounds.min, axis);
            var max = Vector3.Scale(bounds.max, axis);
            var meshTip = max.sqrMagnitude >= min.sqrMagnitude
                ? bounds.center + Vector3.Scale(bounds.extents, axis)
                : bounds.center - Vector3.Scale(bounds.extents, axis);
            return transform.InverseTransformPoint(meshFilter.transform.TransformPoint(meshTip));
        }
    }
}
