using System;
using System.Collections.Generic;
using System.Numerics;
using GUZ.Core.Const;
using GUZ.Core.Extensions;
using GUZ.Core.Models.Vm;
using GUZ.Core.Services;
using GUZ.Core.Services.Vm;
using Reflex.Attributes;
using ZenKit.Daedalus;
using ZenKit.Vobs;

namespace GUZ.Core.Models.Proxy
{
    public class NpcProxy : VirtualObjectProxy, INpc
    {
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly VmService _vmService;

        protected INpc Npc => (INpc)Vob;
        protected bool IsNew;

        /// <summary>
        /// New Npc. Initialized when world is entered for the first time.
        /// </summary>
        public NpcProxy(int index) : base(new ZenKit.Vobs.Npc())
        {
            this.Inject();
            
            IsNew = true;
            InitNew(index);
        }

        /// <summary>
        /// Wrap existing object
        /// </summary>
        public NpcProxy(IVirtualObject vob) : base(vob)
        {
            this.Inject();
                
            if (vob.Ai == null)
            {
                vob.Ai = new AiHuman
                {
                    WalkMode = (int)VmGothicEnums.WalkMode.Walk
                };
            }
            
        }

        private void InitNew(int npcIndex)
        {
            Name = _gameStateService.GothicVm.GetSymbolByIndex(npcIndex)!.Name;
            NpcInstance = Name;

            Ai = new AiHuman
            {
                WalkMode = (int)VmGothicEnums.WalkMode.Walk
            };
            EventManager = new EventManager();
            ModelScale = Vector3.One;
            Attitude = (int)VmGothicEnums.Attitude.Null;
            AttitudeTemp = (int)VmGothicEnums.Attitude.Null;
            
            AddSlot().Name = Constants.SlotRightHand;
            AddSlot().Name = Constants.SlotLeftHand;
            AddSlot().Name = Constants.SlotSword;
            AddSlot().Name = Constants.SlotLongsword;
            AddSlot().Name = Constants.SlotBow;
            AddSlot().Name = Constants.SlotCrossbow;
            AddSlot().Name = Constants.SlotHelmet;
            AddSlot().Name = Constants.SlotTorso;

            for (var i = 0; i < _vmService.TalentsMax; i++)
            {
                AddTalent(new Talent
                {
                    Type = i
                });
            }
        }

        /// <summary>
        /// If an object is initialized for the first time on this world/saveGame, we copy over the init data.
        /// </summary>
        public void CopyFromInstanceData(NpcInstance instance)
        {
            // If we load the world with it's NPCs from SaveGame, we won't copy the data again. (As the data might be different in running game already.
            if (!IsNew)
                return;
            
            Level = instance.Level;
            Xp = instance.Exp;
            XpNextLevel = instance.ExpNext;
            Guild = instance.Guild;
            GuildTrue = instance.Guild;
            FightTactic = instance.FightTactic;
            
            for (var i = 0; i < Enum.GetNames(typeof(DamageType)).Length; i++)
            {
                SetProtection(i, instance.GetProtection((DamageType)i));
            }
            
            for (var i = 0; i < Enum.GetNames(typeof(NpcAttribute)).Length; i++)
            {
                SetAttribute(i, instance.GetAttribute((NpcAttribute)i));
            }
        }
        
        public AiHuman AiHuman =>  (AiHuman)Ai;
        
        public string GetOverlay(int i)
        {
            return Npc.GetOverlay(i);
        }

        public void ClearOverlays()
        {
            Npc.ClearOverlays();
        }

        public void RemoveOverlay(int i)
        {
            Npc.RemoveOverlay(i);
        }

        public void SetOverlay(int i, string overlay)
        {
            Npc.SetOverlay(i, overlay);
        }

        public void AddOverlay(string overlay)
        {
            Npc.AddOverlay(overlay);
        }

        public ITalent GetTalent(int i)
        {
            return Npc.GetTalent(i);
        }

        public void ClearTalents()
        {
            Npc.ClearTalents();
        }

        public void RemoveTalent(int i)
        {
            Npc.RemoveTalent(i);
        }

        public void SetTalent(int i, ITalent talent)
        {
            Npc.SetTalent(i, talent);
        }

        public void AddTalent(ITalent talent)
        {
            Npc.AddTalent(talent);
        }

        public IItem GetItem(int i)
        {
            return Npc.GetItem(i);
        }

        public void ClearItems()
        {
            Npc.ClearItems();
        }

        public void RemoveItem(int i)
        {
            Npc.RemoveItem(i);
        }

        public void SetItem(int i, IItem item)
        {
            Npc.SetItem(i, item);
        }

        public void AddItem(IItem item)
        {
            Npc.AddItem(item);
        }

        public ISlot GetSlot(int i)
        {
            return Npc.GetSlot(i);
        }

        public void ClearSlots()
        {
            Npc.ClearSlots();
        }

        public void RemoveSlot(int i)
        {
            Npc.RemoveSlot(i);
        }

        public ISlot AddSlot()
        {
            return Npc.AddSlot();
        }

        public INews GetNews(int i)
        {
            return Npc.GetNews(i);
        }

        public void ClearNews()
        {
            Npc.ClearNews();
        }

        public void RemoveNews(int i)
        {
            Npc.RemoveNews(i);
        }

        public INews AddNews()
        {
            return Npc.AddNews();
        }

        public int GetProtection(int i)
        {
            return Npc.GetProtection(i);
        }

        public void SetProtection(int i, int v)
        {
            Npc.SetProtection(i, v);
        }

        public int GetAttribute(int i)
        {
            return Npc.GetAttribute(i);
        }

        public void SetAttribute(int i, int v)
        {
            Npc.SetAttribute(i, v);
        }

        public int GetHitChance(int i)
        {
            return Npc.GetHitChance(i);
        }

        public void SetHitChance(int i, int v)
        {
            Npc.SetHitChance(i, v);
        }

        public int GetMission(int i)
        {
            return Npc.GetMission(i);
        }

        public void SetMission(int i, int v)
        {
            Npc.SetMission(i, v);
        }

        public string GetPacked(int i)
        {
            return Npc.GetPacked(i);
        }

        public void SetPacked(int i, string v)
        {
            Npc.SetPacked(i, v);
        }

        public string NpcInstance { get => Npc.NpcInstance; set => Npc.NpcInstance = value; }
        public Vector3 ModelScale { get => Npc.ModelScale; set => Npc.ModelScale = value; }
        public float ModelFatness { get => Npc.ModelFatness; set => Npc.ModelFatness = value; }
        public int Flags { get => Npc.Flags; set => Npc.Flags = value; }
        public int Guild { get => Npc.Guild; set => Npc.Guild = value; }
        public int GuildTrue { get => Npc.GuildTrue; set => Npc.GuildTrue = value; }
        public int Level { get => Npc.Level; set => Npc.Level = value; }
        public int Xp { get => Npc.Xp; set => Npc.Xp = value; }
        public int XpNextLevel { get => Npc.XpNextLevel; set => Npc.XpNextLevel = value; }
        public int Lp { get => Npc.Lp; set => Npc.Lp = value; }
        public int FightTactic { get => Npc.FightTactic; set => Npc.FightTactic = value; }
        public int FightMode { get => Npc.FightMode; set => Npc.FightMode = value; }
        public bool Wounded { get => Npc.Wounded; set => Npc.Wounded = value; }
        public bool Mad { get => Npc.Mad; set => Npc.Mad = value; }
        public int MadTime { get => Npc.MadTime; set => Npc.MadTime = value; }
        public bool Player { get => Npc.Player; set => Npc.Player = value; }
        public string StartAiState { get => Npc.StartAiState; set => Npc.StartAiState = value; }
        public string ScriptWaypoint { get => Npc.ScriptWaypoint; set => Npc.ScriptWaypoint = value; }
        public int Attitude { get => Npc.Attitude; set => Npc.Attitude = value; }
        public int AttitudeTemp { get => Npc.AttitudeTemp; set => Npc.AttitudeTemp = value; }
        public int NameNr { get => Npc.NameNr; set => Npc.NameNr = value; }
        public bool MoveLock { get => Npc.MoveLock; set => Npc.MoveLock = value; }
        public bool CurrentStateValid { get => Npc.CurrentStateValid; set => Npc.CurrentStateValid = value; }
        public string CurrentStateName { get => Npc.CurrentStateName; set => Npc.CurrentStateName = value; }
        public int CurrentStateIndex { get => Npc.CurrentStateIndex; set => Npc.CurrentStateIndex = value; }
        public bool CurrentStateIsRoutine { get => Npc.CurrentStateIsRoutine; set => Npc.CurrentStateIsRoutine = value; }
        public bool NextStateValid { get => Npc.NextStateValid; set => Npc.NextStateValid = value; }
        public string NextStateName { get => Npc.NextStateName; set => Npc.NextStateName = value; }
        public int NextStateIndex { get => Npc.NextStateIndex; set => Npc.NextStateIndex = value; }
        public bool NextStateIsRoutine { get => Npc.NextStateIsRoutine; set => Npc.NextStateIsRoutine = value; }
        public int LastAiState { get => Npc.LastAiState; set => Npc.LastAiState = value; }
        public bool HasRoutine { get => Npc.HasRoutine; set => Npc.HasRoutine = value; }
        public bool RoutineChanged { get => Npc.RoutineChanged; set => Npc.RoutineChanged = value; }
        public bool RoutineOverlay { get => Npc.RoutineOverlay; set => Npc.RoutineOverlay = value; }
        public int RoutineOverlayCount { get => Npc.RoutineOverlayCount; set => Npc.RoutineOverlayCount = value; }
        public int WalkmodeRoutine { get => Npc.WalkmodeRoutine; set => Npc.WalkmodeRoutine = value; }
        public bool WeaponmodeRoutine { get => Npc.WeaponmodeRoutine; set => Npc.WeaponmodeRoutine = value; }
        public bool StartNewRoutine { get => Npc.StartNewRoutine; set => Npc.StartNewRoutine = value; }
        public int AiStateDriven { get => Npc.AiStateDriven; set => Npc.AiStateDriven = value; }
        public Vector3 AiStatePos { get => Npc.AiStatePos; set => Npc.AiStatePos = value; }
        public string CurrentRoutine { get => Npc.CurrentRoutine; set => Npc.CurrentRoutine = value; }
        public bool Respawn { get => Npc.Respawn; set => Npc.Respawn = value; }
        public int RespawnTime { get => Npc.RespawnTime; set => Npc.RespawnTime = value; }
        public int BsInterruptableOverride { get => Npc.BsInterruptableOverride; set => Npc.BsInterruptableOverride = value; }
        public int NpcType { get => Npc.NpcType; set => Npc.NpcType = value; }
        public int SpellMana { get => Npc.SpellMana; set => Npc.SpellMana = value; }
        public IVirtualObject CarryVob { get => Npc.CarryVob; set => Npc.CarryVob = value; }
        public IVirtualObject Enemy { get => Npc.Enemy; set => Npc.Enemy = value; }
        public int OverlayCount => Npc.OverlayCount;
        public List<string> Overlays { get => Npc.Overlays; set => Npc.Overlays = value; }
        public int TalentCount => Npc.TalentCount;
        public List<ITalent> Talents { get => Npc.Talents; set => Npc.Talents = value; }
        public int ItemCount => Npc.ItemCount;
        public List<IItem> Items { get => Npc.Items; set => Npc.Items = value; }
        public int SlotCount => Npc.SlotCount;
        public List<ISlot> Slots => Npc.Slots;
        public int NewsCount => Npc.NewsCount;
        public List<INews> News => Npc.News;
        public List<int> Protection { get => Npc.Protection; set => Npc.Protection = value; }
        public List<int> Attributes { get => Npc.Attributes; set => Npc.Attributes = value; }
        public List<int> HitChance { get => Npc.HitChance; set => Npc.HitChance = value; }
        public List<int> Missions { get => Npc.Missions; set => Npc.Missions = value; }
        public int[] AiVars { get => Npc.AiVars; set => Npc.AiVars = value; }
        public List<string> Packed { get => Npc.Packed; set => Npc.Packed = value; }
    }
}
