using System.Collections.Generic;
using Gothic.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Gothic.VR.Adapters.UI
{
    /// <summary>
    /// Draws a world space UI (dialog box, NPC subtitles, PrintScreen, game menu) on top of walls, furniture and NPC
    /// bodies.
    ///
    /// - The Canvas overrides unity_GUIZTestMode per draw, so graphics get copies of the on-top material assets
    ///   ("Gothic/UI OnTop", ZTest Always). Copies only carry the Gothic texture, which is loaded at runtime.
    /// - Gothic's bitmap fonts are runtime TMP sprite assets. Texts get an on-top copy of their sprite asset; texts
    ///   whose sprite asset is swapped later (hover highlight, PrintScreen fonts) are wrapped again when TMP
    ///   regenerates them - and regenerated once more, as TMP ignores dirty flags set during a Canvas rebuild.
    /// - Menus draw their Gothic background pictures with MeshRenderers. Without depth test, their order comes from
    ///   the render queue: behind the Canvas plane (menu background) < on it (item pictures) < in front (arrows).
    /// </summary>
    public class VRUiOnTop : MonoBehaviour
    {
        [Tooltip("On-top copy for images. Its render queue has to be after all transparents.")]
        [SerializeField] private Material _backgroundMaterial;
        [Tooltip("On-top copy for Gothic font sprites. Render queue after _backgroundMaterial.")]
        [SerializeField] private Material _textMaterial;
        [Tooltip("Optional: on-top copy for MeshRenderers (menu pictures). Render queue before _backgroundMaterial. " +
                 "Meshes on the Canvas plane use queue -1, meshes behind it -2.")]
        [SerializeField] private Material _meshMaterial;
        [Tooltip("Apply on every enable and whenever a menu below gets opened (and again one frame later, after the " +
                 "Start() of new elements). For UIs built by Core code, which can't call Apply().")]
        [SerializeField] private bool _isAppliedAutomatically;
        [Tooltip("0 = always visible. Otherwise hidden when farther away from the camera (meters).")]
        [SerializeField] private float _maxVisibleDistance;
        [Tooltip("Hidden by _maxVisibleDistance. Only needed if _maxVisibleDistance > 0.")]
        [SerializeField] private CanvasGroup _canvasGroup;

        // Distance to the Canvas plane (Canvas units) from which a mesh counts as being in front of/behind it.
        private const float _canvasPlaneTolerance = 0.001f;
        private static readonly int _srcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int _dstBlendId = Shader.PropertyToID("_DstBlend");

        // Shared by all on-top UIs: dialog options and PrintScreen lines are cloned at runtime.
        // Key: source material, on-top material asset, render queue.
        private static readonly Dictionary<(Material, Material, int), Material> _onTopMaterials = new();
        private static readonly Dictionary<TMP_SpriteAsset, TMP_SpriteAsset> _onTopSpriteAssets = new();
        private static readonly HashSet<TMP_SpriteAsset> _onTopSpriteAssetCopies = new();

        private bool _isApplied;
        private int _deferredApplyFrame = -1;
        private Transform _cameraTransform;

        /// <summary>
        /// Patches all graphics and meshes below this GameObject which aren't on top (yet/anymore). Call it again after
        /// cloning new elements or replacing their materials.
        /// Texts are wrapped on every call: UIEvents resets the dialog options to the original font in between.
        /// </summary>
        public void Apply()
        {
            _isApplied = true;
            foreach (var graphic in GetComponentsInChildren<Graphic>(true))
            {
                if (graphic is TMP_Text text)
                    WrapSpriteAsset(text);
                // Its material comes from the (wrapped) sprite asset.
                else if (graphic is not TMP_SubMeshUI && !IsOnTop(graphic.material))
                    graphic.material = GetOnTopMaterial(graphic.material, _backgroundMaterial,
                        _backgroundMaterial.renderQueue);
            }

            if (_meshMaterial == null)
                return;

            foreach (var meshRenderer in GetComponentsInChildren<MeshRenderer>(true))
                PatchMeshRenderer(meshRenderer);
        }

        /// <summary>
        /// DeveloperConfig.EnableHudAlwaysOnTop: the same on-top drawing for a UI built by code, which has no VRUiOnTop
        /// in its prefab (the hero's status bars, the chapter screen). Copies this one's materials once, applies it.
        /// </summary>
        public VRUiOnTop CopyTo(GameObject root)
        {
            if (!root.TryGetComponent<VRUiOnTop>(out var copy))
            {
                copy = root.AddComponent<VRUiOnTop>();
                copy._backgroundMaterial = _backgroundMaterial;
                copy._textMaterial = _textMaterial;
                copy._meshMaterial = _meshMaterial;
            }
            copy.Apply();
            return copy;
        }

        private void OnEnable()
        {
            TMPro_EventManager.TEXT_CHANGED_EVENT.Add(OnTextChanged);

            if (!_isAppliedAutomatically)
                return;

            GlobalEventDispatcher.MenuOpened.AddListener(OnMenuOpened);
            ApplyNowAndNextFrame();
        }

        private void OnDisable()
        {
            TMPro_EventManager.TEXT_CHANGED_EVENT.Remove(OnTextChanged);
            GlobalEventDispatcher.MenuOpened.RemoveListener(OnMenuOpened);
        }

        private void Update()
        {
            if (_maxVisibleDistance <= 0f || _canvasGroup == null)
                return;

            if (_cameraTransform == null)
            {
                var mainCamera = Camera.main;
                if (mainCamera == null)
                    return;
                _cameraTransform = mainCamera.transform;
            }

            var sqrDistance = (_cameraTransform.position - transform.position).sqrMagnitude;
            var isNear = sqrDistance <= _maxVisibleDistance * _maxVisibleDistance;
            _canvasGroup.alpha = isNear ? 1f : 0f;
            _canvasGroup.blocksRaycasts = isNear;
        }

        private void LateUpdate()
        {
            if (_deferredApplyFrame < 0 || Time.frameCount < _deferredApplyFrame)
                return;

            _deferredApplyFrame = -1;
            Apply();
        }

        private void OnMenuOpened(GameObject menu)
        {
            if (menu != null && menu.transform.IsChildOf(transform))
                ApplyNowAndNextFrame();
        }

        /// <summary>
        /// Now: no frame with world geometry in front of the UI. Next frame: elements activated now replace their
        /// materials in Start() (e.g. save game thumbnail, Gothic logo) or get created in Awake() (quest log entries).
        /// </summary>
        private void ApplyNowAndNextFrame()
        {
            Apply();
            _deferredApplyFrame = Time.frameCount + 1;
        }

        /// <summary>
        /// UIEvents swaps the sprite asset on hover (highlight font) - wrap the new one too.
        /// </summary>
        private void OnTextChanged(Object changed)
        {
            if (!_isApplied || changed is not TMP_Text text || !text.transform.IsChildOf(transform))
                return;

            // TMP sends this event while the Canvas rebuilds its graphics. There, the spriteAsset setter can't mark the
            // text dirty, so it would keep the original (depth tested) font material until its next text change.
            if (WrapSpriteAsset(text))
                text.ForceMeshUpdate();
        }

        /// <returns>True, if the text got an on-top sprite asset now.</returns>
        private bool WrapSpriteAsset(TMP_Text text)
        {
            // No own sprite asset (e.g. NPC subtitle lines): TMP falls back to the default one - wrap that.
            var source = text.spriteAsset != null ? text.spriteAsset : TMP_Settings.defaultSpriteAsset;
            if (source == null || _onTopSpriteAssetCopies.Contains(source))
                return false;

            if (!_onTopSpriteAssets.TryGetValue(source, out var onTop) || onTop == null)
            {
                onTop = Instantiate(source);
                onTop.name = source.name; // UIEventsService finds the highlight font by name (..._hi.fnt).
                onTop.material = CreateCopy(_textMaterial, _textMaterial.renderQueue, source.material);
                onTop.UpdateLookupTables();
                _onTopSpriteAssets[source] = onTop;
                _onTopSpriteAssetCopies.Add(onTop);
            }

            text.spriteAsset = onTop;
            return true;
        }

        private void PatchMeshRenderer(MeshRenderer meshRenderer)
        {
            var source = meshRenderer.sharedMaterial;
            // TextMeshPro (3D) texts render with a MeshRenderer too.
            if (source == null || IsOnTop(source) || meshRenderer.TryGetComponent<TMP_Text>(out _)
                || meshRenderer.TryGetComponent<TMP_SubMesh>(out _))
                return;

            var queue = _meshMaterial.renderQueue;
            var canvas = meshRenderer.GetComponentInParent<Canvas>(true);
            if (canvas != null)
            {
                var canvasZ = GetCanvasLocalZ(meshRenderer.transform, canvas.transform);
                if (canvasZ > _canvasPlaneTolerance)
                    queue -= 2;
                else if (canvasZ >= -_canvasPlaneTolerance)
                    queue -= 1;
            }

            var onTop = GetOnTopMaterial(source, _meshMaterial, queue);
            // Opaque Gothic/UI pictures stay opaque, transparent ones keep their blending.
            if (source.HasProperty(_srcBlendId) && source.HasProperty(_dstBlendId))
            {
                onTop.SetFloat(_srcBlendId, source.GetFloat(_srcBlendId));
                onTop.SetFloat(_dstBlendId, source.GetFloat(_dstBlendId));
            }
            meshRenderer.sharedMaterial = onTop;
        }

        /// <summary>
        /// Local z of the object's origin inside the Canvas. Computed from local transforms only: world positions in
        /// Gothic are too large to keep the tiny offsets of menu pictures (e.g. background +0.01) precise.
        /// </summary>
        private static float GetCanvasLocalZ(Transform target, Transform canvas)
        {
            var point = Vector3.zero;
            for (var current = target; current != null && current != canvas; current = current.parent)
                point = current.localPosition + current.localRotation * Vector3.Scale(current.localScale, point);
            return point.z;
        }

        private bool IsOnTop(Material material)
        {
            // Instances of on-top copies (e.g. Renderer.material) are on top as well.
            return material != null && material.shader == _backgroundMaterial.shader;
        }

        /// <summary>
        /// Drops on-top copies whose source material got destroyed (e.g. with its scene) and destroys the copies too.
        /// Copies of living sources stay: persistent UIs (dialog, menu) still use them.
        /// </summary>
        public static void ReleaseDeadMaterials()
        {
            var deadKeys = new List<(Material, Material, int)>();
            foreach (var (key, onTop) in _onTopMaterials)
            {
                if (key.Item1 != null && onTop != null)
                    continue;

                deadKeys.Add(key);
                if (onTop != null)
                    Destroy(onTop);
            }

            foreach (var key in deadKeys)
                _onTopMaterials.Remove(key);
        }

        private static Material GetOnTopMaterial(Material source, Material onTopAsset, int renderQueue)
        {
            var key = (source, onTopAsset, renderQueue);
            if (!_onTopMaterials.TryGetValue(key, out var onTop) || onTop == null)
            {
                onTop = CreateCopy(onTopAsset, renderQueue, source);
                _onTopMaterials[key] = onTop;
            }
            return onTop;
        }

        private static Material CreateCopy(Material onTopAsset, int renderQueue, Material source)
        {
            return new Material(onTopAsset)
            {
                mainTexture = source.mainTexture,
                mainTextureScale = source.mainTextureScale,
                mainTextureOffset = source.mainTextureOffset,
                color = source.color,
                renderQueue = renderQueue
            };
        }
    }
}
