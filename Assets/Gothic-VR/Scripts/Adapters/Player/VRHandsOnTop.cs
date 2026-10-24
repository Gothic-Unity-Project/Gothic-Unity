#if GOTHIC_HVR_INSTALLED
using Gothic.Core.Services.Config;
using Gothic.VR.Services;
using HurricaneVR.Framework.Core.UI;
using Reflex.Attributes;
using UnityEngine;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// Swaps the hand materials to a variant rendered after on-top UI. Controlled by VRUiOnTopService.
    /// </summary>
    public class VRHandsOnTop : MonoBehaviour
    {
        [Inject] private readonly VRUiOnTopService _uiOnTopService;
        [Inject] private readonly ConfigService _configService;

        [SerializeField] private Renderer[] _handRenderers;
        [Tooltip("Hand material which is rendered after the on-top UI.")]
        [SerializeField] private Material _onTopMaterial;
        [Tooltip("Render queue after the on-top UI materials (UiOnTop-Text: 3901). Set in code on a copy: URP's material " +
            "validation resets the queue of the asset to opaque (2000) on every import.")]
        [SerializeField] private int _onTopRenderQueue = 3950;

        private Material[] _originalMaterials;
        private Material _onTopMaterialCopy;

        // DeveloperConfig.EnablePointerLineOnTop: the UI pointer's line (queue 3000) was drawn below the on-top menu
        // (3900) - the hand pointed at a menu entry, the red line ended at the menu's edge.
        private LineRenderer[] _pointerLines;
        private Material[] _pointerLineMaterials;
        private Material[] _pointerLineOnTopCopies;

        private void Start()
        {
            _originalMaterials = new Material[_handRenderers.Length];
            for (var i = 0; i < _handRenderers.Length; i++)
                _originalMaterials[i] = _handRenderers[i].sharedMaterial;
            _onTopMaterialCopy = new Material(_onTopMaterial) { renderQueue = _onTopRenderQueue };
            CollectPointerLines();

            _uiOnTopService.RegisterHands(this);
        }

        private void CollectPointerLines()
        {
            var pointers = transform.root.GetComponentsInChildren<HVRUIPointer>(true);
            _pointerLines = new LineRenderer[pointers.Length];
            _pointerLineMaterials = new Material[pointers.Length];
            _pointerLineOnTopCopies = new Material[pointers.Length];
            for (var i = 0; i < pointers.Length; i++)
            {
                _pointerLines[i] = pointers[i].GetComponent<LineRenderer>();
                if (_pointerLines[i] == null || _pointerLines[i].sharedMaterial == null)
                    continue;
                _pointerLineMaterials[i] = _pointerLines[i].sharedMaterial;
                _pointerLineOnTopCopies[i] = new Material(_pointerLineMaterials[i]) { renderQueue = _onTopRenderQueue };
            }
        }

        private void OnDestroy()
        {
            Destroy(_onTopMaterialCopy);
            if (_pointerLineOnTopCopies == null)
                return;
            foreach (var copy in _pointerLineOnTopCopies)
            {
                if (copy != null)
                    Destroy(copy);
            }
        }

        public void SetOnTop(bool onTop)
        {
            for (var i = 0; i < _handRenderers.Length; i++)
                _handRenderers[i].sharedMaterial = onTop ? _onTopMaterialCopy : _originalMaterials[i];

            if (_pointerLines == null)
                return;
            var isLineOnTop = onTop && _configService.Dev.EnablePointerLineOnTop;
            for (var i = 0; i < _pointerLines.Length; i++)
            {
                if (_pointerLines[i] != null && _pointerLineOnTopCopies[i] != null)
                    _pointerLines[i].sharedMaterial = isLineOnTop ? _pointerLineOnTopCopies[i] : _pointerLineMaterials[i];
            }
        }
    }
}
#endif
