using Gothic.VR.Services;
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

        [SerializeField] private Renderer[] _handRenderers;
        [Tooltip("Hand material which is rendered after the on-top UI.")]
        [SerializeField] private Material _onTopMaterial;
        [Tooltip("Render queue after the on-top UI materials (UiOnTop-Text: 3901). Set in code on a copy: URP's material " +
            "validation resets the queue of the asset to opaque (2000) on every import.")]
        [SerializeField] private int _onTopRenderQueue = 3950;

        private Material[] _originalMaterials;
        private Material _onTopMaterialCopy;

        private void Start()
        {
            _originalMaterials = new Material[_handRenderers.Length];
            for (var i = 0; i < _handRenderers.Length; i++)
                _originalMaterials[i] = _handRenderers[i].sharedMaterial;
            _onTopMaterialCopy = new Material(_onTopMaterial) { renderQueue = _onTopRenderQueue };

            _uiOnTopService.RegisterHands(this);
        }

        private void OnDestroy()
        {
            Destroy(_onTopMaterialCopy);
        }

        public void SetOnTop(bool onTop)
        {
            for (var i = 0; i < _handRenderers.Length; i++)
                _handRenderers[i].sharedMaterial = onTop ? _onTopMaterialCopy : _originalMaterials[i];
        }
    }
}
