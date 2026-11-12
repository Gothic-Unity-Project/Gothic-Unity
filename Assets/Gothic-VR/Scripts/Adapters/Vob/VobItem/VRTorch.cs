using System.Collections;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Vob;
using Gothic.Core.Services.Meshes;
using Gothic.Core.Services.Vobs;
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
    /// engine swapping ItLsTorch for ItLsTorchBurning: fire and light where that item's visual has them (its range,
    /// color and flicker), the light moving with it (the hero light - one at a time: the last torch or Light spell lit
    /// wins, the others keep their fire but go dark), the lighting sound. It keeps burning when let go. Added by
    /// VRVobItem on the first grab. Not saved - torches are unlit after a load.
    /// </summary>
    public class VRTorch : MonoBehaviour
    {
        [Inject] private readonly IVrInput _vrInput;
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly MeshService _meshService;
        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly StationaryLightsService _stationaryLightsService;
        [Inject] private readonly FireService _fireService;

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
            var visual = _fireService.TryGetBurningTorch();
            if (visual == null)
                return;

            var tip = new GameObject("_TorchFire");
            tip.transform.SetParent(transform, false);
            tip.transform.localPosition = GetFireLocalPosition(visual);
            _meshService.CreateVobPfx(visual.FirePfxName, parent: tip);
            _fire = tip;

            // Like a world light (VobInitializerDomain.CreateLight): Gothic's lights have no intensity, the color is
            // the brightness.
            var lightGo = new GameObject("_TorchLight");
            lightGo.transform.SetParent(tip.transform, false);
            _light = lightGo.AddComponent<StationaryLight>();
            _light.Inject();
            _light.Type = LightType.Point;
            _light.Color = visual.LightColor;
            _light.Range = visual.LightRange;
            _light.Intensity = 1f;
            _light.Index = _stationaryLightsService.HeroLightIndex;
            _stationaryLightsService.ClaimHeroLight(this);
            _light.Init();
            StartCoroutine(DriveLight(visual));

            var clip = _audioService.GetRandomSoundClip(Constants.SfxTorchLight);
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
            _stationaryLightsService?.ReleaseHeroLight(this);
            Logger.Log($"[VRTorch] {name} put out", LogCat.VR);
        }

        /// <summary>
        /// The hero light follows the burning tip (like the Light spell in VRSpellFx) and flickers through the
        /// visual's colors - one step per refresh, so the flicker is as fast as the refresh. Another torch or the
        /// Light spell taking the hero light over turns this light off - the fire keeps burning.
        /// </summary>
        private IEnumerator DriveLight(BurningTorchVisual visual)
        {
            while (_light != null)
            {
                var color = visual.GetLightColor(Time.time);
                if (!_stationaryLightsService.UpdateHeroLight(this, _light.transform.position, visual.LightRange,
                        color.linear))
                {
                    Logger.Log($"[VRTorch] {name} lost the hero light - its light is off", LogCat.VR);
                    Destroy(_light.gameObject);
                    _light = null;
                    yield break;
                }
                _light.Color = color;
                _light.Refresh();
                // Immersion setting "Light refresh" (Low/Medium/High).
                yield return new WaitForSeconds(_stationaryLightsService.MovingLightRefreshSeconds);
            }
        }

        /// <summary>
        /// The fire's place from the burning torch's visual - relative to the torch mesh's pivot (the grip), so it
        /// takes the mesh's own transform (Gothic's cm axes and scale) into account.
        /// </summary>
        private Vector3 GetFireLocalPosition(BurningTorchVisual visual)
        {
            var meshFilter = GetComponentInChildren<MeshFilter>();
            var meshTransform = meshFilter != null ? meshFilter.transform : transform;
            return transform.InverseTransformPoint(meshTransform.TransformPoint(visual.FireLocalPosition));
        }
    }
}
