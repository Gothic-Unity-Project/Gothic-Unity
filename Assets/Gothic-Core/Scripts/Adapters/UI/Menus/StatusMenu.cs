using System;
using System.Linq;
using Gothic.Core.Model.UI.Menu;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Vm;
using MyBox;
using Reflex.Attributes;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using Logger = Gothic.Core.Logging.Logger;
using LogCat = Gothic.Core.Logging.LogCat;

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
        [Inject] private readonly ConfigService _configService;

        private const float _cheatClickWindow = 2f;
        private int _levelCheatClicks;
        private float _levelCheatLastTime;
        private int _guildCheatClicks;
        private float _guildCheatLastTime;

        private void Awake()
        {
            InitializeMenu(new MenuInstanceAdapter("MENU_STATUS", null));
        }

        public override void InitializeMenu(AbstractMenuInstance menuInstance)
        {
            base.InitializeMenu(menuInstance);
            SetupCheatTriggers();
        }

        private void OnEnable()
        {
            if (_npcService == null)
                return;
            UpdateData();
        }

        private void OnDisable()
        {
            _levelCheatClicks = 0;
            _guildCheatClicks = 0;
        }

        private void UpdateData()
        {
            var hero = _npcService.GetHeroContainer();
            var vob = hero.Vob;

            var guildId = hero.Props.TrueGuild != VmGothicEnums.Guild.GIL_NONE
                ? (int)hero.Props.TrueGuild
                : vob.Guild;

            Logger.Log($"[StatusMenu] Guild={guildId}({_vmService.GetGuildName(guildId)}) Level={vob.Level} XP={vob.Xp}/{vob.XpNextLevel} LP={vob.Lp} HP={vob.GetAttribute(0)}/{vob.GetAttribute(1)} Mana={vob.GetAttribute(2)}/{vob.GetAttribute(3)} STR={vob.GetAttribute(4)} DEX={vob.GetAttribute(5)}", LogCat.Ui);

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

        private void SetupCheatTriggers()
        {
            if (_configService.Dev.EnableLevel5Cheat)
                AddCheatClickTrigger(_itemNameLevel, _ => OnLevelCheatClick());
            if (_configService.Dev.EnableGuildCheat)
                AddCheatClickTrigger(_itemNameGuild, _ => OnGuildCheatClick());
        }

        private void AddCheatClickTrigger(string itemName, UnityAction<BaseEventData> callback)
        {
            if (!MenuItemCache.TryGetValue(itemName, out var cached)) return;
            var go = cached.go;
            var tmp = go.GetComponentInChildren<TMP_Text>();
            if (tmp != null) tmp.raycastTarget = true;
            var trigger = go.GetComponent<EventTrigger>();
            if (trigger == null) trigger = go.AddComponent<EventTrigger>();
            var clickEntry = new EventTrigger.Entry { eventID = EventTriggerType.PointerClick };
            clickEntry.callback.AddListener(callback);
            trigger.triggers.Add(clickEntry);
        }

        public void TriggerLevelCheatClick() => OnLevelCheatClick();
        public void TriggerGuildCheatClick() => OnGuildCheatClick();
        public void ExecuteLevelCheat() => CheatAddLevels();
        public void ExecuteGuildCheat() => CheatToNovice();

        private void OnLevelCheatClick()
        {
            if (!_configService.Dev.EnableLevel5Cheat) return;
            var now = Time.unscaledTime;
            if (now - _levelCheatLastTime > _cheatClickWindow)
                _levelCheatClicks = 0;
            _levelCheatClicks++;
            _levelCheatLastTime = now;
            if (_levelCheatClicks >= 5)
            {
                _levelCheatClicks = 0;
                CheatAddLevels();
            }
        }

        private void OnGuildCheatClick()
        {
            if (!_configService.Dev.EnableGuildCheat) return;
            var now = Time.unscaledTime;
            if (now - _guildCheatLastTime > _cheatClickWindow)
                _guildCheatClicks = 0;
            _guildCheatClicks++;
            _guildCheatLastTime = now;
            if (_guildCheatClicks >= 3)
            {
                _guildCheatClicks = 0;
                CheatToNovice();
            }
        }

        protected override void StartItem(string itemName, string commandName)
        {
            if (itemName == _itemNameLevel) OnLevelCheatClick();
            else if (itemName == _itemNameGuild) OnGuildCheatClick();
        }

        private void CheatAddLevels()
        {
            const int levelsToAdd = 5;
            var hero = _npcService.GetHeroContainer();
            var oldLevel = hero.Instance.Level;

            hero.Instance.Level += levelsToAdd;
            hero.Instance.Lp += levelsToAdd * 10;

            var hpMax = hero.Vob.GetAttribute(1) + levelsToAdd * 12;
            hero.Vob.SetAttribute(1, hpMax);
            hero.Vob.SetAttribute(0, hpMax);

            _npcService.SyncHeroInstanceToVob();
            UpdateData();
            Logger.Log($"[StatusMenu] Cheat: level {oldLevel}→{hero.Instance.Level} (+{levelsToAdd * 10} LP, +{levelsToAdd * 12} HP_MAX)", LogCat.Ui);
        }

        private void CheatToNovice()
        {
            var hero = _npcService.GetHeroContainer();
            hero.Props.TrueGuild = VmGothicEnums.Guild.GIL_NOV;
            UpdateData();
            Logger.Log("[StatusMenu] Cheat: guild set to GIL_NOV", LogCat.Ui);
        }

        protected override void Close(string itemName, string commandName) { }
        protected override void ConsoleCommand(string itemName, string commandName) { }
        protected override void PlaySound(string itemName, string commandName) { }
        protected override void ExecuteCommand(string itemName, string commandName) { }
    }
}
