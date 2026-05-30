#if GUZ_HVR_INSTALLED
using System.Collections.Generic;
using GUZ.Core;
using GUZ.Core.Adapters.UI;
using GUZ.Core.Extensions;
using GUZ.Core.Logging;
using GUZ.Core.Manager;
using GUZ.Core.Models.Caches;
using GUZ.Core.Models.Marvin;
using GUZ.Core.Services.Caches;
using GUZ.VR.Services;
using Reflex.Attributes;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Logger = GUZ.Core.Logging.Logger;

namespace GUZ.VR.Adapters.Marvin
{
    public class MarvinInspectorTabHandler : MonoBehaviour
    {
        [SerializeField] private TMP_Text _objectTextComp;
        [SerializeField] private RectTransform _contentTransform;
        [SerializeField] private ToggleButton _chooseVobButton;
        [SerializeField] private ToggleButton _selectHeroButton;


        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly MarvinService _marvinService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;

        private const int _propertyHeight = 15;
        private const int _propertyMarginBottom = 10;
        private const int _propertyLabelWidth = 300;
        private List<object> _marvinProperties;
        private int _propertyCount;
        
        
        private void Update()
        {
            if (!_marvinService.IsMarvinSelectionMode)
                return;

            FillMarvinSelection();
        }

        /// <summary>
        /// Next Hand grab will be selecting the VOB to inspect.
        /// </summary>
        public void OnChooseVobClick()
        {
            _marvinService.IsMarvinSelectionMode = true;
            _marvinService.MarvinSelectionGO = null;

            _chooseVobButton.SetActive();
            _selectHeroButton.SetInactive();
        }

        /// <summary>
        /// Immediately "select" Hero
        /// </summary>
        public void OnSelectHeroClick()
        {
            _marvinService.IsMarvinSelectionMode = true;
            _marvinService.MarvinSelectionGO = _vrPlayerService.VRContextInteractionService.GetCurrentPlayerController();

            _selectHeroButton.SetActive();
            _chooseVobButton.SetInactive();
        }

        private void FillMarvinSelection()
        {
            var go = _marvinService.MarvinSelectionGO;

            // Wait until an object is selected.
            if (go == null)
                return;

            // Reset first. If we have errors below to ensure normal gameplay is reactivated.
            _marvinService.IsMarvinSelectionMode = false;
            _chooseVobButton.SetInactive();

            var propertyCollectors = go.GetComponentsInChildren<IMarvinPropertyCollector>();
            _marvinProperties = new();

            if (propertyCollectors.IsEmpty())
            {
                _marvinProperties.Add(new MarvinPropertyHeader("No property found!"));
            }

            foreach (var collector in propertyCollectors)
            {
                var properties = collector.CollectMarvinInspectorProperties();
                _marvinProperties.AddRange(properties);
            }

            CreateFields();
        }

        private void CreateFields()
        {
            _objectTextComp.text = _marvinService.MarvinSelectionGO.name;
            _propertyCount = 0;

            if (_contentTransform.childCount != 0)
                Destroy(_contentTransform.GetChild(0).gameObject);

            var rootMargin = new GameObject("Margin");
            rootMargin.SetParent(_contentTransform.gameObject);
            // left: 50% of text move to right + one empty entry. top: one empty entry space.
            rootMargin.transform.localPosition = new Vector2(_propertyLabelWidth / 2f + _propertyHeight, -_propertyHeight);
            float y = 0;
            
            foreach (var property in _marvinProperties)
            {
                var newElement = new GameObject();
                newElement.SetParent(rootMargin);

                y = -(_propertyHeight / 2f) - _propertyHeight * _propertyCount - _propertyMarginBottom * _propertyCount;
                
                switch (property)
                {
                    case MarvinPropertyHeader header:
                        newElement.name = header.Name;
                        var headerGo = CreateField(header, newElement, y);
                        headerGo.GetComponentInChildren<TMP_Text>().fontStyle = FontStyles.Underline | FontStyles.UpperCase;
                        break;
                    case MarvinProperty<bool> boolProperty:
                        newElement.name = boolProperty.Name;
                        CreateField(boolProperty, newElement, y);
                        break;
                    case MarvinProperty<int> intProperty:
                        newElement.name = intProperty.Name;
                        CreateField(intProperty, newElement, y);
                        break;
                    case MarvinProperty<float> floatProperty:
                        newElement.name = floatProperty.Name;
                        CreateField(floatProperty, newElement, y);
                        break;
                    default:
                        Logger.LogWarning($"Unhandled MarvinSelection property type: {property.GetType()}", LogCat.Debug);
                        break;
                }
                _propertyCount++;
            }
            
            // + 2.5 * _propertyHeight -> Grant more spaze at the bottom to ensure we can read all properties.
            _contentTransform.sizeDelta = new Vector2(_contentTransform.sizeDelta.x, Mathf.Abs(y) + 2.5f * _propertyHeight);
        }

        private GameObject CreateField(MarvinPropertyHeader header, GameObject rootGo, float y)
        {
            var headerGo = _resourceCacheService.TryGetPrefabObject(PrefabType.UiDebugText, parent: rootGo);
            headerGo.GetComponentInChildren<TMP_Text>().text = header.Name;
            
            var headerTransform = headerGo!.GetComponent<RectTransform>();
            headerTransform.anchorMin = new Vector2(0, 1);
            headerTransform.anchorMax = new Vector2(0, 1);
            headerTransform.localPosition = new Vector2(0, y);
            headerTransform.sizeDelta = new Vector2(_propertyLabelWidth, _propertyHeight);

            return headerGo;
        }

        private GameObject CreateField(MarvinProperty<bool> boolProperty, GameObject rootGo, float y)
        {
            var labelGo = CreateField(new MarvinPropertyHeader(boolProperty.Name), rootGo, y);

            var toggleGo = _resourceCacheService.TryGetPrefabObject(PrefabType.UiDebugToggle, parent: rootGo);
            
            var toggleTransform = toggleGo!.GetComponent<RectTransform>();
            toggleTransform.anchorMin = new Vector2(0, 1);
            toggleTransform.anchorMax = new Vector2(0, 1);
            toggleTransform.localPosition = new Vector2(labelGo.GetComponent<RectTransform>().sizeDelta.x, y);
            toggleTransform.sizeDelta = new Vector2(_propertyLabelWidth, _propertyHeight);

            var toggleComp = toggleGo.GetComponentInChildren<Toggle>();
            toggleComp.isOn = boolProperty.Getter();
            toggleComp.onValueChanged.AddListener(value => boolProperty.Setter(value));
            
            return toggleGo;
        }
        
        private GameObject CreateField(MarvinProperty<int> intProperty, GameObject rootGo, float y)
        {
            var marvinLabel = new MarvinProperty<float>(intProperty.Name, () => (float)intProperty.Getter(),
                value => intProperty.Setter((int)value), intProperty.MinValue, intProperty.MaxValue);
            var sliderGo = CreateField(marvinLabel, rootGo, y);

            // The only difference to float handling!
            sliderGo.GetComponentInChildren<Slider>().wholeNumbers = true;

            return sliderGo;
        }

        private GameObject CreateField(MarvinProperty<float> floatProperty, GameObject rootGo, float y)
        {
            // Label + (InitialValue -> Remove trailing zeroes but show up to 3 fraction elements)
            var labelGo = CreateField(new MarvinPropertyHeader($"{floatProperty.Name} ({floatProperty.Getter():0.###})"), rootGo, y);
            var labelWidth = labelGo.GetComponent<RectTransform>().sizeDelta.x;
            
            // Slider
            var sliderGo = _resourceCacheService.TryGetPrefabObject(PrefabType.UiDebugSlider, parent: rootGo);

            var sliderTransform = sliderGo!.GetComponent<RectTransform>();
            sliderTransform.anchorMin = new Vector2(0, 1);
            sliderTransform.anchorMax = new Vector2(0, 1);
            sliderTransform.localPosition = new Vector2(labelWidth, y);
            sliderTransform.sizeDelta = new Vector2(_propertyLabelWidth, _propertyHeight);

            var sliderComp = sliderGo.GetComponentInChildren<Slider>();
            sliderComp.minValue = floatProperty.MinValue;
            sliderComp.maxValue = floatProperty.MaxValue;

            sliderComp.wholeNumbers = false;
            
            // Current Slider value
            var valueGo = CreateField(new MarvinPropertyHeader(string.Empty), rootGo, y);
            // Align right of Slider
            valueGo.transform.localPosition = new Vector2(labelWidth + _propertyLabelWidth + _propertyMarginBottom, y);

            sliderComp.onValueChanged.AddListener(value =>
            {
                floatProperty.Setter(value);
                valueGo.GetComponentInChildren<TMP_Text>().text = value.ToString("0.###");
            });
            sliderComp.value = floatProperty.Getter();
            
            return sliderGo;
        }
    }
}
#endif
