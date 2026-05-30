using System;
using System.Linq;
using GUZ.Core.Const;
using GUZ.Core.Model.UI.Menu;
using GUZ.Core.Services.Vm;
using MyBox;
using Reflex.Attributes;
using TMPro;

namespace GUZ.Core.Adapters.UI.Menus
{
    public class StatusMenu : AbstractMenu
    {
        private string _itemNameGuild = "MENU_ITEM_PLAYERGUILD";
        private string _itemNameLevel = "MENU_ITEM_LEVEL";
        private string _itemNameExp = "MENU_ITEM_EXP";
        private string _itemNameLevelNext = "MENU_ITEM_LEVEL_NEXT";
        private string _itemNameLearn = "MENU_ITEM_LEARN";

        private string _itemNameAttributePattern = "MENU_ITEM_ATTRIBUTE_{0}";

        private string _itemNameArmorPattern = "MENU_ITEM_ARMOR_{0}";

        private string _itemTalentTitlePattern = "MENU_ITEM_TALENT_{0}_TITLE";
        private string _itemTalentSkillPattern = "MENU_ITEM_TALENT_{0}_SKILL";
        private string _itemTalentDescriptionPattern = "MENU_ITEM_TALENT_{0}";

        
        [Inject] private readonly VmService _vmService;

        
        private void Awake()
        {
            InitializeMenu(new MenuInstanceAdapter("MENU_STATUS", null));
        }

        public override void InitializeMenu(AbstractMenuInstance menuInstance)
        {
            base.InitializeMenu(menuInstance);
            Setup();
        }

        private void Setup()
        {
            UpdateData();
        }

        /// <summary>
        /// Fill data. Currently demo data.
        /// </summary>
        private void UpdateData()
        {
            MenuItemCache[_itemNameGuild].go.GetComponentInChildren<TMP_Text>().text = "TestGuild";
            MenuItemCache[_itemNameLevel].go.GetComponentInChildren<TMP_Text>().text = "100";
            MenuItemCache[_itemNameExp].go.GetComponentInChildren<TMP_Text>().text = "1337";
            MenuItemCache[_itemNameLevelNext].go.GetComponentInChildren<TMP_Text>().text = "13";
            MenuItemCache[_itemNameLearn].go.GetComponentInChildren<TMP_Text>().text = "42";

            Enumerable.Range(1, 4).ForEach(i =>
            {
                var key = string.Format(_itemNameAttributePattern, i);
                MenuItemCache[key].go.GetComponentInChildren<TMP_Text>().text = $"{i}/100";
            });

            Enumerable.Range(1, 4).ForEach(i =>
            {
                var key = string.Format(_itemNameArmorPattern, i);
                MenuItemCache[key].go.GetComponentInChildren<TMP_Text>().text = $"{i}";
            });

            var talentTitles = _vmService.TalentTitles;
            var talentSkills = _vmService.TalentSkills;

            Enumerable.Range(0, 12).ForEach(i =>
            {
                var keyTitle = string.Format(_itemTalentTitlePattern, i+1);
                var keySkill = string.Format(_itemTalentSkillPattern, i+1);
                var keyDescription = string.Format(_itemTalentDescriptionPattern, i+1);

                var randValue = new Random().Next(0, 2);
                var skillDescriptionText = talentSkills[i];

                string skillDescriptionFormatted;
                if (skillDescriptionText.IsNullOrEmpty() || skillDescriptionText== "|")
                {
                    skillDescriptionFormatted = "";
                }
                else
                {
                    skillDescriptionFormatted = skillDescriptionText.Split("|")[randValue];
                }

                MenuItemCache[keyTitle].go.GetComponentInChildren<TMP_Text>().text = talentTitles[i];
                MenuItemCache[keySkill].go.GetComponentInChildren<TMP_Text>().text = skillDescriptionFormatted;

                if (MenuItemCache.TryGetValue(keyDescription, out var item))
                {
                    item.go.GetComponentInChildren<TMP_Text>().text = $"{randValue}%";
                }
            });
        }

        protected override void Undefined(string itemName, string commandName)
        {
            throw new NotImplementedException();
        }

        protected override void StartMenu(string itemName, string commandName)
        {
            throw new NotImplementedException();
        }

        protected override void StartItem(string itemName, string commandName)
        {
            throw new NotImplementedException();
        }

        protected override void Close(string itemName, string commandName)
        {
            throw new NotImplementedException();
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
    }
}
