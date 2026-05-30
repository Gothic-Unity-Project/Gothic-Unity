using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GUZ.Core.Adapters.UI
{
    /// <summary>
    /// Add a state to the button. Reflected in a consistent color as Pressed, also after UnitUI has another element in focus.
    /// 
    /// Understanding of reused button states and their colors:
    /// * Normal - Off, but usable
    /// * Highlighted - Hovered over
    /// * Pressed - Currently "press" button down
    /// * Selected - After "press" is released. Active until another UI element is clicked (Pressed).
    /// * Disabled - Non-usable
    /// </summary>
    public class ToggleButton : Button
    {
        public bool IsActiveState { get; private set; }

        // Colors used from: https://www.foundations.unity.com/fundamentals/color-palette#Buttons
        // Hint: When using these dark themed button colors, change text color too: --unity-colors-button-text
        private static readonly ColorBlock _unityDarkColors = new()
        {
            normalColor      = new Color32(88, 88, 88, 255), // --unity-colors-button-background
            highlightedColor = new Color32(103, 103, 103, 255), // --unity-colors-button-background-hover
            pressedColor     = new Color32(79, 104, 127, 255), // --unity-colors-button-background-hover_pressed
            selectedColor    = new Color32(70, 96, 124, 255), // --unity-colors-button-background-pressed
            disabledColor    = ColorBlock.defaultColorBlock.disabledColor,
            colorMultiplier    = 1.0f,
            fadeDuration       = 0.1f
        };

        
        
        private ColorBlock _normalColors;
        private ColorBlock _activeColors;

        protected override void Awake()
        {
            base.Awake();

            _normalColors = colors;
            _activeColors = new ColorBlock
            {
                // Changed value: Whenever the "Pressed" state is removed from button (i.e., another UI element is clicked),
                // then we keep this color to symbolize the Active state.
                normalColor = colors.pressedColor,

                highlightedColor = colors.highlightedColor,
                pressedColor = colors.pressedColor,
                selectedColor = colors.selectedColor,
                disabledColor = colors.disabledColor,
                colorMultiplier = colors.colorMultiplier,
                fadeDuration = colors.fadeDuration
            };
        }
        
        public void ToggleState()
        {
            if (IsActiveState)
                SetInactive();
            else
                SetActive();
        }
        
        public void SetActive()
        {
            IsActiveState = true;
            colors = _activeColors;
        }
        
        public void SetInactive()
        {
            IsActiveState = false;
            colors = _normalColors;
        }

        /// <summary>
        /// Handle state toggle automatically. No need to handle it via OnClick event.
        /// </summary>
        public override void OnSelect(BaseEventData eventData)
        {
            base.OnSelect(eventData);
            
            if (currentSelectionState == SelectionState.Selected)
                ToggleState();
        }
    }
}
