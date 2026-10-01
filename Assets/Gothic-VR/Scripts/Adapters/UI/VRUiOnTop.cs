#if GOTHIC_HVR_INSTALLED
using System.Collections.Generic;
using Gothic.Core.Logging;
using HurricaneVR.Framework.Core.Grabbers;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.UI
{
    /// <summary>
    /// Draws a world space UI (dialog box, NPC subtitles) on top of walls, furniture and NPC bodies.
    ///
    /// - Material values can't do it: the Canvas overrides unity_GUIZTestMode per draw, and the dialog background
    ///   (URP Unlit) has no depth test parameter. All graphics get the "Gothic/UI OnTop" shader (ZTest Always).
    /// - Gothic's bitmap fonts are TMP sprite assets (TMP_SubMeshUI with the asset's material, re-assigned on every text
    ///   change). Labels get an on-top copy of their sprite asset. UIEvents swaps sprite assets on hover (highlight font)
    ///   - LateUpdate wraps those again. The copy keeps the original name, as the highlight font is looked up by name.
    /// - Queues: after all transparents (Gothic alpha materials are sorted back to front and one closer to the camera
    ///   would cover the UI), text after its background.
    /// </summary>
    public class VRUiOnTop : MonoBehaviour
    {
        private const int _backgroundRenderQueue = 3900;
        private const int _textRenderQueue = 3901;
        private const int _handsRenderQueue = 3950;

        // Resolved on first use - Shader.Find isn't allowed in a MonoBehaviour's (static) field initializers.
        private static Shader _onTopShader;
        private static readonly Dictionary<TMP_SpriteAsset, TMP_SpriteAsset> _onTopSpriteAssets = new();
        private static readonly List<(Material material, int queue)> _handMaterials = new();
        private static bool _areHandsOnTop;

        private readonly HashSet<Graphic> _patchedGraphics = new();
        private readonly List<TMP_Text> _texts = new();


        /// <summary>
        /// Patches all not yet patched graphics below this GameObject (new dialog options are cloned later on).
        /// </summary>
        public void Apply()
        {
            foreach (var graphic in GetComponentsInChildren<Graphic>(true))
            {
                if (!_patchedGraphics.Add(graphic))
                    continue;

                if (graphic is TMP_Text text)
                {
                    _texts.Add(text);
                    WrapSpriteAsset(text);
                    continue;
                }

                // Its material comes from the (patched) sprite asset.
                if (graphic is TMP_SubMeshUI)
                    continue;

                var original = graphic.material;
                graphic.material = CreateOnTopMaterial(graphic.mainTexture, original.color, _backgroundRenderQueue);
            }
        }

        /// <summary>
        /// VR hands above on-top UI, so we see them when pointing at a dialog option. Only while a dialog is open -
        /// otherwise they covered letters/documents held in front of them.
        /// </summary>
        public static void SetHandsOnTop(bool onTop)
        {
            if (_areHandsOnTop == onTop)
                return;
            _areHandsOnTop = onTop;

            if (onTop)
            {
                _handMaterials.Clear();
                foreach (var hand in FindObjectsByType<HVRHandGrabber>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    var handModel = hand.HandModel != null ? hand.HandModel : hand.transform;
                    foreach (var handRenderer in handModel.GetComponentsInChildren<Renderer>(true))
                    {
                        foreach (var material in handRenderer.materials)
                        {
                            _handMaterials.Add((material, material.renderQueue));
                            material.renderQueue = _handsRenderQueue;
                        }
                    }
                }
                Logger.Log($"[VRUiOnTop] {_handMaterials.Count} hand material(s) moved to queue {_handsRenderQueue}.", LogCat.VR);
                return;
            }

            foreach (var (material, queue) in _handMaterials)
            {
                if (material != null)
                    material.renderQueue = queue;
            }
            _handMaterials.Clear();
        }

        private void LateUpdate()
        {
            foreach (var text in _texts)
            {
                if (text != null)
                    WrapSpriteAsset(text);
            }
        }

        private static void WrapSpriteAsset(TMP_Text text)
        {
            var source = text.spriteAsset;
            if (source == null || _onTopSpriteAssets.ContainsValue(source))
                return;

            if (!_onTopSpriteAssets.TryGetValue(source, out var onTop) || onTop == null)
            {
                onTop = Instantiate(source);
                onTop.name = source.name; // UIEventsService finds the highlight font by name (..._hi.fnt).
                onTop.material = CreateOnTopMaterial(source.material.mainTexture, Color.white, _textRenderQueue);
                onTop.UpdateLookupTables();
                _onTopSpriteAssets[source] = onTop;
            }

            text.spriteAsset = onTop;
        }

        private static Material CreateOnTopMaterial(Texture texture, Color color, int renderQueue)
        {
            if (_onTopShader == null)
                _onTopShader = Shader.Find("Gothic/UI OnTop");

            return new Material(_onTopShader)
            {
                mainTexture = texture,
                color = color,
                renderQueue = renderQueue
            };
        }
    }
}
#endif
