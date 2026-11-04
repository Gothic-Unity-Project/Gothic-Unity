using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Gothic.VR.Adapters.UI
{
    /// <summary>
    /// One line of the trade window (VRTradeWindow): item name with amount on the left, its price on the right.
    /// </summary>
    public class VRTradeRow : MonoBehaviour
    {
        [SerializeField] private Button _button;
        [SerializeField] private TMP_Text _name;
        [SerializeField] private TMP_Text _price;

        [Tooltip("Text alpha of an item the hero can't afford.")]
        [SerializeField] private float _disabledAlpha = 0.4f;

        /// <summary>
        /// Instance name of the item shown, null while the row is empty.
        /// </summary>
        public string ItemName { get; private set; }

        public Button Button => _button;

        public void Show(string itemName, string label, int price, bool isPossible)
        {
            ItemName = itemName;
            gameObject.SetActive(true);
            _button.interactable = isPossible;
            _name.text = label;
            _price.text = price.ToString();
            var alpha = isPossible ? 1f : _disabledAlpha;
            _name.alpha = alpha;
            _price.alpha = alpha;
        }

        public void Clear()
        {
            ItemName = null;
            gameObject.SetActive(false);
        }
    }
}
