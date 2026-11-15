using System;
using Gothic.Core.Logging;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Components;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.UI.Menus
{
    /// <summary>
    /// A controller drawing with a label per stick and button (help panels above the menu). Each time it's shown, the
    /// stick and face button labels show what that control does in the player's current layout (left-handed, jump,
    /// crouch and menu button); grip and trigger labels show this page's text for them. A control without text is
    /// hidden together with its line.
    /// </summary>
    public class VRControllerHelpPage : MonoBehaviour
    {
        [Serializable]
        public class SlotLabel
        {
            [Tooltip("The controller this label points at.")]
            public VrHandSide Side;

            [Tooltip("The stick or button this label points at.")]
            public VrControlSlot Slot;

            [Tooltip("The label's localized text.")]
            public LocalizeStringEvent Label;

            [Tooltip("Optional: the line from the label to the control, hidden with the label.")]
            public GameObject Line;
        }

        [Serializable]
        public class RoleText
        {
            [Tooltip("What the control does in the current layout.")]
            public VrControlRole Role;

            [Tooltip("The text shown at the control that has this role. Empty: the control isn't labeled.")]
            public LocalizedString Text;

            [Tooltip("Optional: the text when the role sits on a stick click (e.g. \"Click: Jump\"). Empty: Text.")]
            public LocalizedString ClickText;
        }

        [Tooltip("All labels of the drawing, one per controller and control.")]
        [SerializeField] private SlotLabel[] _slotLabels = Array.Empty<SlotLabel>();

        [Tooltip("Texts for the stick and face buttons, by what they do. Roles without an entry aren't labeled.")]
        [SerializeField] private RoleText[] _roleTexts = Array.Empty<RoleText>();

        [Tooltip("Text at both triggers. Empty: the trigger labels are hidden.")]
        [SerializeField] private LocalizedString _triggerText = new();

        [Tooltip("Text at both grips. Empty: the grip labels are hidden.")]
        [SerializeField] private LocalizedString _gripText = new();

        // Bound in the player's scope by the VR backend. Missing (e.g. a scope without the player): default layout.
        [Inject] private readonly IVrInput _vrInput;

        private void OnEnable()
        {
            var layout = _vrInput?.ControlLayout ?? VrControlLayout.Default;

            foreach (var slotLabel in _slotLabels)
            {
                if (slotLabel?.Label == null)
                {
                    Logger.LogWarning($"[HelpPanel] {name}: a slot label has no LocalizeStringEvent.", LogCat.VR);
                    continue;
                }

                var text = GetText(layout, slotLabel);
                var isShown = text != null && !text.IsEmpty;

                slotLabel.Label.gameObject.SetActive(isShown);
                if (slotLabel.Line != null)
                    slotLabel.Line.SetActive(isShown);

                if (!isShown)
                    continue;

                // A copy per label: both hands' labels may show the same text, and a LocalizedString notifies every
                // label it was given to.
                slotLabel.Label.StringReference = new LocalizedString(text.TableReference, text.TableEntryReference);
                slotLabel.Label.RefreshString();
            }
        }

        private LocalizedString GetText(VrControlLayout layout, SlotLabel slotLabel)
        {
            switch (slotLabel.Slot)
            {
                case VrControlSlot.Trigger:
                    return _triggerText;
                case VrControlSlot.Grip:
                    return _gripText;
            }

            var role = layout.GetRole(slotLabel.Side, slotLabel.Slot);
            if (role == VrControlRole.None)
                return null;

            foreach (var roleText in _roleTexts)
            {
                if (roleText == null || roleText.Role != role)
                    continue;

                var isClick = slotLabel.Slot == VrControlSlot.JoystickButton;
                return isClick && roleText.ClickText != null && !roleText.ClickText.IsEmpty
                    ? roleText.ClickText
                    : roleText.Text;
            }

            return null;
        }
    }
}
