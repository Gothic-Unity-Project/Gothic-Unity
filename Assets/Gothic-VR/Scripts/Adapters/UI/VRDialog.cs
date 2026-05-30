#if GUZ_HVR_INSTALLED
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GUZ.Core.Adapters.UI;
using GUZ.Core.Extensions;
using GUZ.Core.Const;
using GUZ.Core.Manager;
using GUZ.Core.Models.Dialog;
using MyBox;
using Reflex.Attributes;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using ZenKit.Daedalus;

namespace GUZ.VR.Adapters.UI
{
    public class VRDialog : MonoBehaviour
    {
        [Inject] private readonly DialogService _dialogService;

        [SerializeField] private GameObject _dialogRoot;
        [SerializeField] private List<GameObject> _dialogItems;
        [SerializeField] private UIEvents _uiEventsHandler;

        [NonSerialized]
        public List<string> CurrentDialogOptionTexts;
        
        private float _dialogItemHeight;
        private int _dialogItemsInUse;

        private void Awake()
        {
            // When prefab is loaded for the first time, we store the size of a dialog item.
            // It's needed to add more elements on top in the right position.
            if (_dialogItemHeight == 0f)
            {
                var rectTransform = _dialogItems.First().GetComponent<RectTransform>();
                _dialogItemHeight = rectTransform.rect.height;
            }

            // The whole dialog topic will be enabled later during gameplay.
            gameObject.SetActive(false);
        }

        public void StartDialogInitially()
        {
            gameObject.SetActive(true); // If we enable it earlier, Billboard Comp on this GO is calculating all the time.
        }

        public void EndDialog()
        {
            gameObject.SetActive(false); // Disable whole Dialog menu (Audio, UI, Billboard)
            _dialogRoot.SetParent(SceneManager.GetSceneByName(Constants.ScenePlayer).GetRootGameObjects()[0], worldPositionStays: true);
        }

        public void ShowDialog(GameObject npcGo)
        {
            var npcDialog = npcGo.FindChildRecursively("DialogMenuRootPos");
            _dialogRoot.SetParent(npcDialog, true, true, true);

            var rootRectHeight = _dialogItemHeight * _dialogItemsInUse;
            _dialogRoot.GetComponent<RectTransform>().SetHeight(rootRectHeight);

            // Some HoverEnter events recognize the DialogItems itself, some recognize the Labels directly. We therefore need to add both to be checked.
            var hoverElements = new List<GameObject>();
            hoverElements.AddRange(_dialogItems);
            hoverElements.AddRange(_dialogItems.Select(i => i.GetComponentInChildren<TMP_Text>().gameObject).ToList());
            _uiEventsHandler.SetElementsToHover(hoverElements, true);
            
            StartCoroutine(ShowDialogWithDelay());
        }

        private IEnumerator ShowDialogWithDelay()
        {
            // Skipping first frame of when dialog activates to let it fully rotate towards Player Camera
            yield return new WaitForEndOfFrame();
            _dialogRoot.SetActive(true);
        }

        /// <summary>
        /// Once we close the dialog, we need to move the dialog box back to the General scene
        /// (or something without any object which might be destroyed (like an NPC after dying)).
        /// </summary>
        public void HideDialog()
        {
            _dialogRoot.SetActive(false);
        }
        
        public void FillDialog(NpcInstance instance, List<DialogOption> dialogOptions)
        {
            CreateAdditionalDialogOptions(dialogOptions.Count);
            ClearDialogOptions();

            // G1 handles DialogOptions added via (Info_AddChoice()) in reverse order.
            dialogOptions.Reverse();
            for (var i = 0; i < dialogOptions.Count; i++)
            {
                var dialogItem = _dialogItems[i];
                var dialogOption = dialogOptions[i];

                dialogItem.GetComponent<Button>().onClick.AddListener(
                    () => OnDialogClicked(instance, dialogOption.Function));
                dialogItem.FindChildRecursively("Label").GetComponent<TMP_Text>().text = dialogOption.Text;
            }

            CurrentDialogOptionTexts = dialogOptions.Select(i => i.Text).ToList();
            _dialogItemsInUse = dialogOptions.Count;
        }

        public void FillDialog(NpcInstance instance, List<InfoInstance> dialogOptions)
        {
            CreateAdditionalDialogOptions(dialogOptions.Count);
            ClearDialogOptions();
            
            for (var i = 0; i < dialogOptions.Count; i++)
            {
                var dialogItem = _dialogItems[i];
                var dialogOption = dialogOptions[i];

                dialogItem.GetComponent<Button>().onClick.AddListener(
                    () => OnDialogClicked(instance, dialogOption));
                dialogItem.FindChildRecursively("Label").GetComponent<TMP_Text>().text = dialogOption.Description;
            }
            
            CurrentDialogOptionTexts = dialogOptions.Select(i => i.Description).ToList();
            _dialogItemsInUse = dialogOptions.Count;
        }
        
        /// <summary>
        /// We won't know the maximum amount of element from the start of the game.
        /// But we don't want to show pagination or so but instead want to show them all in one spot.
        /// Therefore, we start with one entry as template and create more if needed now.
        /// </summary>
        private void CreateAdditionalDialogOptions(int currentItemsNeeded)
        {
            var newItemsToCreate = currentItemsNeeded - _dialogItems.Count;

            if (newItemsToCreate <= 0)
            {
                return;
            }

            var firstItem = _dialogItems.First();
            for (var i = 0; i < newItemsToCreate; i++)
            {
                var newItem = Instantiate(firstItem, firstItem.transform.parent, false);
                _dialogItems.Add(newItem);

                newItem.name = $"Item{_dialogItems.Count - 1:00}";
                
                var newYPos = -_dialogItemHeight/2 + -_dialogItemHeight * (_dialogItems.Count - 1);
                newItem.GetComponent<RectTransform>().SetPositionY(newYPos);
            }
        }

        private void ClearDialogOptions()
        {
            foreach (var item in _dialogItems)
            {
                item.GetComponent<Button>().onClick.RemoveAllListeners();
                item.FindChildRecursively("Label").GetComponent<TMP_Text>().text = "";
            }
        }

        public void DialogSelected(int index)
        {
            if (index < 0 || _dialogItems.Count < index)
                return;
            
            _dialogItems[index].GetComponent<Button>().onClick.Invoke();
        }
        
        private void OnDialogClicked(NpcInstance instance, InfoInstance infoInstance)
        {
            _dialogService.MainSelectionClicked(instance.GetUserData(), infoInstance);
        }

        private void OnDialogClicked(NpcInstance instance, int informationId)
        {
            _dialogService.SubSelectionClicked(instance.GetUserData(), informationId);
        }
    }
}
#endif
