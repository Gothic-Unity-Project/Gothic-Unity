using System.Collections.Generic;
using Gothic.Core;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Const;
using Gothic.Core.Logging;
using Gothic.Core.Services.Config;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Services;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// DeveloperConfig.EnableHeroHands + Immersion option "Hero's hands": the model's own hands (fists, with the
    /// armor's gloves) are the VR hands - the backend's hand visuals are hidden meanwhile. Also the held items' attach
    /// points towards the fists' palms and the arms drawn over on-top UI. Worker of VRHeroBody (new().Inject(),
    /// Initialize, Dispose).
    /// </summary>
    public class VrHeroHands
    {
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly VRPlayerService _vrPlayerService;

        private static readonly VrHandSide[] _handSides = { VrHandSide.Left, VrHandSide.Right };

        private readonly List<Renderer> _hiddenVrHandRenderers = new();
        private readonly HashSet<Transform> _hiddenVrHandVisuals = new();

        // After the on-top UI (UiOnTop-Text: 3901), like VRHandsOnTop.
        private const int _armsOnTopRenderQueue = 3950;
        private bool _areArmsOnTop;
        private readonly Dictionary<Renderer, Material[]> _armsOriginalMaterials = new();
        private readonly Dictionary<Material, Material> _onTopMaterialCopies = new();

        // The fists' palm slots (left, right) and the attach points' spots.
        private readonly Transform[] _palmSlots = new Transform[2];
        private readonly Dictionary<Transform, Vector3> _attachDefaultPositions = new();
        private readonly Dictionary<Transform, Vector3> _attachShiftedPositions = new();
        private const float _maxHoldShift = 0.04f;

        /// <summary>
        /// The option is on (DeveloperConfig.EnableHeroHands and the Immersion option "Hero's hands").
        /// </summary>
        public bool IsOptionOn { get; private set; }

        /// <summary>
        /// The model's hands are shown, the backend's hand visuals hidden.
        /// </summary>
        public bool AreShown { get; private set; }

        public void Initialize()
        {
            ReadHeroHandsOption();
            GlobalEventDispatcher.PlayerPrefUpdated.AddListener(OnPlayerPrefUpdated);
        }

        public void Dispose()
        {
            GlobalEventDispatcher.PlayerPrefUpdated.RemoveListener(OnPlayerPrefUpdated);
            foreach (var copy in _onTopMaterialCopies.Values)
            {
                if (copy != null)
                    Object.Destroy(copy);
            }
            _onTopMaterialCopies.Clear();
        }

        private void OnPlayerPrefUpdated(string key, object value)
        {
            if (key == VRConstants.IniNames.HeroHands)
                ReadHeroHandsOption();
        }

        private void ReadHeroHandsOption()
        {
            IsOptionOn = _configService.Dev.EnableHeroHands && _configService.Gothic != null &&
                         _configService.Gothic.GetInt(VRConstants.IniNames.HeroHands,
                             VRConstants.HeroHandsDefaultValue) == 1;
        }

        /// <summary>
        /// The backend's hand visuals hidden or shown again. False if nothing changed (the VR hands are hidden again
        /// anyway - a hand visual can switch its renderers on again: hover/grab poses).
        /// </summary>
        public bool SetShown(bool isShown, IVrRig rig)
        {
            if (isShown == AreShown)
            {
                if (isShown)
                    HideVrHands(rig); // A hand visual can switch its renderers on again (hover/grab poses).
                return false;
            }

            AreShown = isShown;
            if (isShown)
                HideVrHands(rig);
            else
                RestoreVrHands();
            Logger.Log($"[VRHeroBody] Hero's hands {(isShown ? "shown - VR hands hidden" : "hidden - VR hands back")}",
                LogCat.VR);
            return true;
        }

        /// <summary>
        /// Only the hand model's own renderers, collected once per hand visual. Whatever is attached later (spell VFX -
        /// thrown on as the projectile, item popups, the held item) stays visible: hiding them every frame left the
        /// charged spell and the flying projectile without their effect, just the light.
        /// </summary>
        private void HideVrHands(IVrRig rig)
        {
            for (var s = 0; s < _handSides.Length; s++)
            {
                var handVisual = rig?.GetHandVisual(_handSides[s]);
                if (handVisual == null || !_hiddenVrHandVisuals.Add(handVisual))
                    continue;
                foreach (var vrRenderer in handVisual.GetComponentsInChildren<Renderer>(true))
                {
                    if (vrRenderer.enabled && IsVrHandModelRenderer(vrRenderer) &&
                        !_hiddenVrHandRenderers.Contains(vrRenderer))
                        _hiddenVrHandRenderers.Add(vrRenderer);
                }
            }

            // A hand visual can switch its renderers on again (hover/grab poses).
            for (var i = 0; i < _hiddenVrHandRenderers.Count; i++)
            {
                var vrRenderer = _hiddenVrHandRenderers[i];
                if (vrRenderer != null && vrRenderer.enabled)
                    vrRenderer.enabled = false;
            }
        }

        private void RestoreVrHands()
        {
            foreach (var vrRenderer in _hiddenVrHandRenderers)
            {
                if (vrRenderer != null)
                    vrRenderer.enabled = true;
            }
            _hiddenVrHandRenderers.Clear();
            _hiddenVrHandVisuals.Clear();
        }

        private static bool IsVrHandModelRenderer(Renderer vrRenderer)
        {
            // Not what the hand holds (items, the ring's spell rune), no effects or texts.
            return vrRenderer is MeshRenderer or SkinnedMeshRenderer &&
                   vrRenderer.GetComponent<TMPro.TMP_Text>() == null &&
                   vrRenderer.GetComponentInParent<VobLoader>() == null;
        }

        /// <summary>
        /// The new body's palm slots (ZS_LEFTHAND, ZS_RIGHTHAND); the attach points shift anew.
        /// </summary>
        public void SetPalmSlots(Dictionary<string, Transform> bones)
        {
            _palmSlots[0] = bones.TryGetValue(Constants.SlotLeftHand, out var leftSlot) ? leftSlot : null;
            _palmSlots[1] = bones.TryGetValue(Constants.SlotRightHand, out var rightSlot) ? rightSlot : null;
            _attachShiftedPositions.Clear();
        }

        /// <summary>
        /// With the hero's fists, held items sit a bit more towards the fingers
        /// (HeroHandsHoldShift of the way to the fist's palm slot ZS_RIGHTHAND/ZS_LEFTHAND, at most _maxHoldShift) - at
        /// the block hand's attach point a coin lay on the knuckles. The shift is taken once when the fists show and
        /// stays fixed (moving the attach point every frame broke grabbing); back to its own spot without the fists.
        /// </summary>
        public void UpdateHeldItemAttachPoints(bool isInPalm)
        {
            var hands = _vrPlayerService.VRContextInteractionService?.PhysicsHands;
            if (hands == null)
                return;

            for (var i = 0; i < hands.Count; i++)
            {
                var hand = hands[i];
                var attach = hand != null ? hand.AttachPoint : null;
                if (attach == null)
                    continue;
                if (!_attachDefaultPositions.TryGetValue(attach, out var defaultPosition))
                {
                    defaultPosition = attach.localPosition;
                    _attachDefaultPositions[attach] = defaultPosition;
                }

                var slot = _palmSlots[hand.Side == VrHandSide.Left ? 0 : 1];
                if (!isInPalm || slot == null || attach.parent == null)
                {
                    _attachShiftedPositions.Remove(attach);
                    if (attach.localPosition != defaultPosition)
                        attach.localPosition = defaultPosition;
                    continue;
                }

                if (!_attachShiftedPositions.TryGetValue(attach, out var shiftedPosition))
                {
                    var defaultWorld = attach.parent.TransformPoint(defaultPosition);
                    var shift = Vector3.ClampMagnitude((slot.position - defaultWorld) *
                                                       _configService.Dev.HeroHandsHoldShift, _maxHoldShift);
                    shiftedPosition = attach.parent.InverseTransformPoint(defaultWorld + shift);
                    _attachShiftedPositions[attach] = shiftedPosition;
                    Logger.Log($"[VRHeroBody] {hand.Side} hand holds {shift.magnitude * 100f:F1} cm towards the " +
                               "fingers", LogCat.VR);
                }
                if (attach.localPosition != shiftedPosition)
                    attach.localPosition = shiftedPosition;
            }
        }

        /// <summary>
        /// The physics hands live in the Player scene and outlive this world's body: their attach points go back to
        /// their own spots - the next body took the shifted spot as the default and shifted it again (items crept
        /// forward in the hand with every world load).
        /// </summary>
        public void RestoreHeldItemAttachPoints()
        {
            foreach (var (attach, defaultPosition) in _attachDefaultPositions)
            {
                if (attach != null)
                    attach.localPosition = defaultPosition;
            }
            _attachDefaultPositions.Clear();
            _attachShiftedPositions.Clear();
        }

        /// <summary>
        /// With the hero's fists as the VR hands, the arms (fists included) draw
        /// over on-top UI like the block hands (VRHandsOnTop) while a menu or dialog wants the hands on top - the fist
        /// pointing at a menu entry was hidden behind the menu. Copies of their materials with a later render queue.
        /// </summary>
        public void SetArmsOnTop(bool isOnTop, IReadOnlyList<SkinnedMeshRenderer> armRenderers)
        {
            if (isOnTop == _areArmsOnTop)
                return;
            _areArmsOnTop = isOnTop;

            if (!isOnTop)
            {
                foreach (var (renderer, materials) in _armsOriginalMaterials)
                {
                    if (renderer != null)
                        renderer.sharedMaterials = materials;
                }
                _armsOriginalMaterials.Clear();
                return;
            }

            for (var i = 0; i < armRenderers.Count; i++)
            {
                var renderer = armRenderers[i];
                if (renderer == null || _armsOriginalMaterials.ContainsKey(renderer))
                    continue;

                var materials = renderer.sharedMaterials;
                _armsOriginalMaterials[renderer] = materials;
                var onTop = new Material[materials.Length];
                for (var m = 0; m < materials.Length; m++)
                    onTop[m] = GetOnTopCopy(materials[m]);
                renderer.sharedMaterials = onTop;
            }
        }

        private Material GetOnTopCopy(Material material)
        {
            if (material == null)
                return null;
            if (!_onTopMaterialCopies.TryGetValue(material, out var copy) || copy == null)
            {
                copy = new Material(material) { renderQueue = _armsOnTopRenderQueue };
                _onTopMaterialCopies[material] = copy;
            }
            return copy;
        }
    }
}
