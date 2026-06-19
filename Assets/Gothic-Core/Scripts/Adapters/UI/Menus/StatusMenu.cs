using System;
using System.Linq;
using Gothic.Core.Model.UI.Menu;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Vm;
using MyBox;
using Reflex.Attributes;
using TMPro;

namespace Gothic.Core.Adapters.UI.Menus
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

        // Attribute slot → (currentIndex, maxIndex); maxIndex -1 means single value
        // Gothic engine convention: 1=Strength(4), 2=Dexterity(5), 3=Mana(2/max3), 4=HP(0/max1)
        private static readonly int[] _attrCurrentIndex = { 4, 5, 2, 0 };
        private static readonly int[] _attrMaxIndex     = {-1,-1, 3, 1 };

        // Armor slot → DamageType index: 1=Blunt, 2=Point(projectiles), 3=Fire, 4=Magic
        // DamageType values: Blunt=1, Point=3, Fire=4, Magic=6
        private static readonly int[] _armorProtIndex = { 1, 3, 4, 6 };

        [Inject] private readonly VmService _vmService;
        [Inject] private readonly NpcService _npcService;

        private void Awake()
        {
            InitializeMenu(new MenuInstanceAdapter("MENU_STATUS", null));
        }

        private void OnEnable()
        {
            if (_npcService == null)
                return;
            UpdateData();
        }

        private void UpdateData()
        {
            var hero = _npcService.GetHeroContainer();
            var vob = hero.Vob;

            var guildId = hero.Instance.Guild;

            MenuItemCache[_itemNameGuild].go.GetComponentInChildren<TMP_Text>().text = _vmService.GetGuildName(guildId);
            MenuItemCache[_itemNameLevel].go.GetComponentInChildren<TMP_Text>().text = vob.Level.ToString();
            MenuItemCache[_itemNameExp].go.GetComponentInChildren<TMP_Text>().text = vob.Xp.ToString();
            MenuItemCache[_itemNameLevelNext].go.GetComponentInChildren<TMP_Text>().text = vob.XpNextLevel.ToString();
            MenuItemCache[_itemNameLearn].go.GetComponentInChildren<TMP_Text>().text = vob.Lp.ToString();

            Enumerable.Range(0, 4).ForEach(i =>
            {
                var key = string.Format(_itemNameAttributePattern, i + 1);
                var cur = vob.GetAttribute(_attrCurrentIndex[i]);
                var text = _attrMaxIndex[i] >= 0
                    ? $"{cur}/{vob.GetAttribute(_attrMaxIndex[i])}"
                    : cur.ToString();
                MenuItemCache[key].go.GetComponentInChildren<TMP_Text>().text = text;
            });

            Enumerable.Range(0, 4).ForEach(i =>
            {
                var key = string.Format(_itemNameArmorPattern, i + 1);
                MenuItemCache[key].go.GetComponentInChildren<TMP_Text>().text = vob.GetProtection(_armorProtIndex[i]).ToString();
            });

            var talentTitles = _vmService.TalentTitles;
            var talentSkills = _vmService.TalentSkills;

            Enumerable.Range(0, talentTitles.Count).ForEach(i =>
            {
                var keyTitle = string.Format(_itemTalentTitlePattern, i + 1);
                var keySkill = string.Format(_itemTalentSkillPattern, i + 1);
                var keyDescription = string.Format(_itemTalentDescriptionPattern, i + 1);

                if (!MenuItemCache.ContainsKey(keyTitle))
                    return;

                var talent = vob.GetTalent(i);
                var skillText = talentSkills[i];
                string skillFormatted;
                if (skillText.IsNullOrEmpty() || skillText == "|")
                {
                    skillFormatted = string.Empty;
                }
                else
                {
                    var parts = skillText.Split("|");
                    var partIndex = Math.Min(talent.Skill, parts.Length - 1);
                    skillFormatted = parts[partIndex];
                }

                MenuItemCache[keyTitle].go.GetComponentInChildren<TMP_Text>().text = talentTitles[i];
                MenuItemCache[keySkill].go.GetComponentInChildren<TMP_Text>().text = skillFormatted;

                if (MenuItemCache.TryGetValue(keyDescription, out var descItem))
                    descItem.go.GetComponentInChildren<TMP_Text>().text = $"{talent.Value}%";
            });
        }

        protected override void Undefined(string itemName, string commandName) { }
        protected override void StartMenu(string itemName, string commandName) { }
        protected override void StartItem(string itemName, string commandName) { }
        protected override void Close(string itemName, string commandName) { }
        protected override void ConsoleCommand(string itemName, string commandName) { }
        protected override void PlaySound(string itemName, string commandName) { }
        protected override void ExecuteCommand(string itemName, string commandName) { }
    }
}
