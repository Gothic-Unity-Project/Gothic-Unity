using System;
using System.IO;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Model.UI.Menu;
using Gothic.Core.Models.Caches;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.World;
using Gothic.Services.UI;
using Reflex.Attributes;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using ZenKit;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Adapters.UI.Menus
{
    public class SaveMenu : AbstractMenu
    {
        private const string EmptySlotText = "---";
        private const int _titleMaxLength = 30;

        private bool _isInitialized;
        private GameObject[] SaveSlots = new GameObject[16];

        private GameObject Thumbnail;
        private TMP_Text World;
        private TMP_Text SavedAt;
        private TMP_Text GameTime;
        private TMP_Text PlayTime;
        private TMP_Text Version;

        private readonly SaveGame[] _saves = new SaveGame[16];

        [SerializeField] private bool _isSaving;
        private bool _isLoading => !_isSaving;

        private string _saveLoadStatus;

        
        [Inject] private readonly TextureCacheService _textureCacheService;
        [Inject] private readonly SaveGameService _saveGameService;
        [Inject] private readonly BootstrapService _bootstrapService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;
        [Inject] private readonly ContextTextInputService _contextTextInputService;
        [Inject] private readonly LocalizationService _localizationService;

        
        /// <summary>
        /// Pre-fill the Load Game entries with names and textures (if existing)
        /// </summary>
        private void Start()
        {
            Thumbnail.GetComponent<MeshRenderer>().material =
                TextureService.GetEmptyMaterial(MaterialExtension.BlendMode.Opaque);
        }

        private void OnEnable()
        {
            // InitializeMenu() is called after first OnEnable().
            if (!_isInitialized)
                return;
            
            FillSaveGameEntries();
        }
        
        public override void InitializeMenu(AbstractMenuInstance menuInstance)
        {
            base.InitializeMenu(menuInstance);
            Setup();

            _isInitialized = true;
            FillSaveGameEntries();
        }

        private void Setup()
        {
            _saveLoadStatus = _isSaving ? "SAVE" : "LOAD";

            var thumbnailGo = MenuItemCache["MENUITEM_LOADSAVE_THUMBPIC"].go;
            Thumbnail = _resourceCacheService.TryGetPrefabObject(PrefabType.UiThumbnail, name: "Thumbnail",
                parent: thumbnailGo)!;

            Thumbnail.transform.localPosition = Vector3.zero;
            Thumbnail.transform.localRotation = Quaternion.Euler(270, 0, 0);

            World = MenuItemCache["MENUITEM_LOADSAVE_LEVELNAME_VALUE"].go.GetComponent<TMP_Text>();
            SavedAt = MenuItemCache["MENUITEM_LOADSAVE_DATETIME_VALUE"].go.GetComponent<TMP_Text>();
            GameTime = MenuItemCache["MENUITEM_LOADSAVE_GAMETIME_VALUE"].go.GetComponent<TMP_Text>();
            PlayTime = MenuItemCache["MENUITEM_LOADSAVE_PLAYTIME_VALUE"].go.GetComponent<TMP_Text>();
            // Version = this.transform.FindChildRecursively("MENUITEM_LOADSAVE_LEVELNAME_VALUE")?.GetComponent<TMP_Text>();

            for (int i = 1; i <= 15; i++)
            {
                var saveSlot = MenuItemCache[$"MENUITEM_{_saveLoadStatus}_SLOT{i}"].go;

                var text = saveSlot.GetComponentInChildren<TMP_Text>();
                text.text = EmptySlotText; // daedalus menu item has text "unknown", but in game it shows as ---
                SaveSlots[i] = saveSlot;
            }

            FillSaveGameEntries();
        }

        protected override void Undefined(string itemName, string commandName)
        {
            return;
        }

        protected override void StartMenu(string itemName, string commandName)
        {
            MenuHandler.OpenMenu(commandName);
        }

        protected override void StartItem(string itemName, string commandName)
        {
            SaveLoadGame(commandName);
        }

        protected override void Close(string itemName, string commandName)
        {
            if (commandName == "SAVEGAME_LOAD")
            {
                SaveLoadGame(itemName);
            }

            MenuHandler.ToggleVisibility();
        }

        protected override void ConsoleCommand(string itemName, string commandName)
        {
            throw new NotImplementedException();
        }

        protected override void PlaySound(string itemName, string commandName)
        {
            throw new NotImplementedException();
        }

        protected override void ExecuteCommand(string itemName, string commandName)
        {
            throw new NotImplementedException();
        }

        protected override bool IsMenuItemActive(string menuItemName)
        {
            return (MenuItemCache[menuItemName].item.Flags & MenuItemFlag.Disabled) == 0;
        }

        private void FillSaveGameEntries()
        {
            var saveGameListPath = _saveGameService.SavesFolderPath;
            if (!Directory.Exists(saveGameListPath))
                return;

            _saveGameService.RecoverInterruptedSaves();

            foreach (var fullPath in Directory.EnumerateDirectories(saveGameListPath))
            {
                var saveGameFolderName = Path.GetFullPath(fullPath).Remove(0, saveGameListPath.Length + 1);

                if (!saveGameFolderName.StartsWith("savegame"))
                {
                    continue;
                }

                // G1 save games start with ID 1
                if (!int.TryParse(saveGameFolderName.Remove(0, "savegame".Length), out var folderSaveId))
                    continue;

                // Our menu prefab shows 15 slots (G1 layout); G2 installations can hold savegame16-20.
                // Skip out-of-range folders instead of crashing the whole menu init on the array access.
                // FIXME - Add proper 20 slots for G2 in GameObjects. (Why are they 16 for G1? Maybe 15 with missing 0 index?)
                if (folderSaveId < 1 || folderSaveId >= SaveSlots.Length || SaveSlots[folderSaveId] == null)
                {
                    Logger.LogWarning($"[SaveMenu] Save folder '{saveGameFolderName}' has no UI slot for G2. Skipping for now.",
                        LogCat.Ui);
                    continue;
                }

                // Load metadata. A save written by another engine/mod version can fail parsing - skip it instead of
                // aborting the whole menu.
                SaveGame save;
                try
                {
                    save = _saveGameService.GetSaveGame((SaveGameService.SlotId)folderSaveId);
                }
                catch (Exception e)
                {
                    Logger.LogWarning($"[SaveMenu] Save '{saveGameFolderName}' couldn't be parsed: {e.Message}",
                        LogCat.Ui);
                    continue;
                }
                if (save?.Metadata == null)
                    continue;

                _saves[folderSaveId] = save;

                // Set metadata to slot
                var saveSlotGo = SaveSlots[folderSaveId];

                var eventTrigger = saveSlotGo.GetComponent<EventTrigger>();

                var pointerEnterEntry = eventTrigger.triggers.Find(x => x.eventID == EventTriggerType.PointerEnter);
                pointerEnterEntry.callback.AddListener(_ => OnLoadGameSlotPointerEnter(folderSaveId));

                var pointerExitEntry = eventTrigger.triggers.Find(x => x.eventID == EventTriggerType.PointerExit);
                pointerExitEntry.callback.AddListener(_ => OnLoadGameSlotPointerExit());

                saveSlotGo.GetComponentInChildren<TMP_Text>().text =
                    string.IsNullOrWhiteSpace(save.Metadata.Title) ? EmptySlotText : save.Metadata.Title;
            }
        }

        public void OnLoadGameSlotPointerEnter(int id)
        {
            var save = _saves[id];

            if (save == null)
            {
                return;
            }

            Thumbnail.GetComponent<MeshRenderer>().material.mainTexture
                = _textureCacheService.TryGetTexture(save.Thumbnail, "savegame_" + save.Metadata.Title);
            Thumbnail.SetActive(true);
            World.text = save.Metadata.World;
            SavedAt.text = save.Metadata.SaveDate;
            GameTime.text = $"{save.Metadata.TimeDay} - {save.Metadata.TimeHour}:{save.Metadata.TimeMinute}";
            var playTime = save.Metadata.PlayTime;
            PlayTime.text = $"{(int)playTime.TotalHours}:{playTime.Minutes:00}";
            // Version.text = save.Metadata.VersionAppName;
        }

        public void OnLoadGameSlotPointerExit()
        {
            Thumbnail.SetActive(false);
            World.text = "";
            SavedAt.text = "";
            GameTime.text = "";
            PlayTime.text = "";
            // Version.text = "";
        }

        public void SaveLoadGame(string inputName)
        {
            string numberPart = inputName.Substring($"MENUITEM_{_saveLoadStatus}_SLOT".Length);

            if (!int.TryParse(numberPart, out int id))
            {
                Logger.LogError($"Save Game Not Found for {inputName}. Using New game instead.", LogCat.Loading);
                id = (int)SaveGameService.SlotId.NewGame;
            }
            if (_isLoading)
            {
                var save = _saves[id];

                if (save == null)
                {
                    return;
                }

                // Can be triggered from Scene:mainMenu or Scene:AnyWorld, therefore removing active scene.
                if (!_bootstrapService.LoadWorld(save.Metadata.World, (SaveGameService.SlotId)id,
                        SceneManager.GetActiveScene().name))
                    ShowComment("menuitem.load.failed");
            }
            else
            {
                // Like Gothic: no saving while dead or in a dialog. The reason shows in the comment line.
                if (!_saveGameService.CanSave(out var blockReason))
                {
                    ShowComment(blockReason);
                    return;
                }

                // Name the save (Gothic asks too). Empty names save too, as in the original.
                // The slot shows the typed name, like the original's inline edit.
                var slotText = SaveSlots[id].GetComponentInChildren<TMP_Text>();
                var slotTextBefore = slotText.text;
                var currentTitle = _saves[id]?.Metadata?.Title ?? string.Empty;
                ShowTypedTitle(slotText, currentTitle);
                _contextTextInputService.RequestText(currentTitle, _titleMaxLength,
                    title => SaveToSlot(id, title),
                    () => slotText.text = slotTextBefore,
                    title => ShowTypedTitle(slotText, title));
            }
        }

        private static void ShowTypedTitle(TMP_Text slotText, string title)
        {
            slotText.text = title + "_";
        }

        private void SaveToSlot(int id, string title)
        {
            // The thumbnail needs the end of the frame (and the keyboard closed).
            _saveGameService.SaveCurrentGameAtEndOfFrame((SaveGameService.SlotId)id, title, isSaved =>
            {
                // E.g. a dialog started while the keyboard was open.
                if (!isSaved)
                    ShowComment(_saveGameService.CanSave(out var blockReason) ? "menuitem.save.failed" : blockReason);

                FillSaveGameEntries();
            });
        }

        private void ShowComment(string localizationKey)
        {
            if (Comment != null)
                Comment.text = _localizationService.GetText(localizationKey);
        }
    }
}
