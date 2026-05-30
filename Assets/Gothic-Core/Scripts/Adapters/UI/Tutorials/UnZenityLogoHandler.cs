using UnityEngine;
using UnityEngine.UI;

namespace GUZ.Core.Adapters.UI
{
    public class UnZenityLogoHandler : MonoBehaviour
    {
        [SerializeField]
        private GameObject _tutorialHandler;

        [SerializeField]
        private float _minBrightness = 0.7f;
        [SerializeField]
        private float _maxBrightness = 1.3f;
        [SerializeField]
        private float _pulsationSpeed = 2.0f;

        [SerializeField]
        private RawImage _targetImage;

        private Color _originalColor;
        private Material _materialInstance;

        private void Start()
        {
            _originalColor = _targetImage.color;
            
            // Create a material instance to avoid affecting other UI elements
            if (_targetImage.material == null)
                _materialInstance = new Material(Shader.Find("UI/Default"));
            else
                _materialInstance = new Material(_targetImage.material);
            
            _targetImage.material = _materialInstance;
        }

        
        private void Update()
        {
            // Calculate brightness based on sine wave
            var brightness = Mathf.Lerp(_minBrightness, _maxBrightness, 
                (Mathf.Sin(Time.time * _pulsationSpeed) + 1) * 0.5f);
            
            // Apply brightness to color
            var newColor = _originalColor * brightness;
            newColor.a = _originalColor.a; // Preserve original alpha
            
            _targetImage.color = newColor;
        }

        
        public void OnClick()
        {
            if (_tutorialHandler == null)
                return;
            
            _tutorialHandler.gameObject.SetActive(!_tutorialHandler.gameObject.activeSelf);
        }
        
        private void OnDestroy()
        {
            // Clean up material instance
            if (_materialInstance != null)
                Destroy(_materialInstance);
        }

    }
}
