using System;
using System.Linq;
using Gothic.Core.Logging;
using Gothic.Core.Model.UI.Menu;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Vm;
using MyBox;
using Reflex.Attributes;
using TMPro;
using Logger = Gothic.Core.Logging.Logger;

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

        // Armor slot → protection index (G1 constants.d PROT_*): 1=weapons → PROT_EDGE(2), 2=projectiles → PROT_POINT(6),
        // 3=fire → PROT_FIRE(3), 4=magic → PROT_MAGIC(5). (BLUNT=1, FLY=4 aren't shown.)
        private static readonly int[] _armorProtIndex = { 2, 6, 3, 5 };

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
            _npcService.SyncHeroInstanceToVob();
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
            var talentCount = Math.Min(talentTitles.Count, vob.TalentCount);

            // Talent 0 doesn't exist (TXT_TALENTS[0] = ""), NPC_TALENT_1H = 1 is shown in MENU_ITEM_TALENT_1_*.
            // Menu row number == talent index. Previously row i+1 got talent i, which shifted everything by one.
            var filledRows = 0;
            Enumerable.Range(1, Math.Max(0, talentCount - 1)).ForEach(i =>
            {
                var keyTitle = string.Format(_itemTalentTitlePattern, i);
                var keySkill = string.Format(_itemTalentSkillPattern, i);
                var keyDescription = string.Format(_itemTalentDescriptionPattern, i);

                if (!MenuItemCache.ContainsKey(keyTitle))
                    return;

                filledRows++;
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

                SetItemText(keyTitle, talentTitles[i]);
                SetItemText(keySkill, skillFormatted);
                // Optional: e.g. G1 shows hit chance / failure chance, mods often comment it out for some rows.
                if (MenuItemCache.ContainsKey(keyDescription))
                    SetItemText(keyDescription, $"{talent.Value}%");
            });

            Logger.Log($"[StatusMenu] Talents: count={vob.TalentCount}, titles={talentTitles.Count}, rows filled={filledRows}", LogCat.Ui);
        }

        /// <summary>
        /// A missing menu item or text component must not abort filling all the remaining rows.
        /// </summary>
        private void SetItemText(string key, string text)
        {
            if (!MenuItemCache.TryGetValue(key, out var item) || item.go == null)
            {
                Logger.LogWarning($"[StatusMenu] Menu item '{key}' not found.", LogCat.Ui);
                return;
            }

            var textComp = item.go.GetComponentInChildren<TMP_Text>(true);
            if (textComp == null)
            {
                Logger.LogWarning($"[StatusMenu] Menu item '{key}' has no text component.", LogCat.Ui);
                return;
            }

            textComp.text = text;
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
