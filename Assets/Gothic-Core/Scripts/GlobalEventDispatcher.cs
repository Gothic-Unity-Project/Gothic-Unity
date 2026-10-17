using System;
using Gothic.Core.Adapters.Npc;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Context;
using Gothic.Core.Models.Doc;
using Gothic.Core.Models.UI;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using ZenKit;
using ZenKit.Daedalus;
using ZenKit.Vobs;

namespace Gothic.Core
{
    /// <summary>
    /// Loading/Unloading order of scenes:
    /// https://github.com/Gothic-Unity-Project/Gothic-Unity/blob/main/Docs/development/diagrams/SceneLoading.drawio.png
    /// </summary>
    public static class GlobalEventDispatcher
    {
        // We need to ensure, that other modules will register themselves based on current Control+GameMode setting.
        // Since we can't call them (e.g. Flat/VR) directly, we need to leverage this IoC pattern.
        public static readonly UnityEvent<Controls> RegisterControlsService = new();
        public static readonly UnityEvent<GameVersion> RegisterGameVersionService = new();

        // Events are named in order of execution during a normal game play.
        public static readonly UnityEvent PlayerSceneLoaded = new();
        public static readonly UnityEvent ZenKitBootstrapped = new();
        public static readonly UnityEvent MainMenuSceneLoaded = new();
        public static readonly UnityEvent LoadingSceneLoaded = new();
        public static readonly UnityEvent WorldSceneLoaded = new();

        public static readonly UnityEvent<DateTime> GameTimeSecondChangeCallback = new();
        public static readonly UnityEvent<DateTime> GameTimeMinuteChangeCallback = new();
        public static readonly UnityEvent<DateTime> GameTimeHourChangeCallback = new();

        public static readonly UnityEvent<GameObject> MusicZoneEntered = new();
        public static readonly UnityEvent<GameObject> MusicZoneExited = new();
        public static readonly UnityEvent<string, string> LevelChangeTriggered = new();
        
        public static readonly UnityEvent GothicInisInitialized = new();
        public static readonly UnityEvent<string, object> PlayerPrefUpdated = new();
        
        public static readonly UnityEvent LoadGameStart = new();
        
        
        public static readonly UnityEvent<NpcContainer, NpcLoader, bool, bool> NpcMeshCullingChanged = new();
        public static readonly UnityEvent<GameObject> VobMeshCullingChanged = new();

        public static readonly UnityEvent<INpc> CreateNpc = new();

        
        // Fight events
        public enum HandSide
        {
            None  = 0,
            Left  = 1,
            Right = 2,
            Both  = 3
        }

        /// <summary>
        /// Attack window state transitions. Fired by AttackWindowStateMachine.
        /// NpcContainer is the combatant whose attack window changed (player or NPC/Monster).
        /// </summary>
        public static readonly UnityEvent<NpcContainer> FightWindowInitial = new();
        public static readonly UnityEvent<NpcContainer> FightWindowComboFailed = new();
        public static readonly UnityEvent<NpcContainer> FightWindowAttack = new();
        public static readonly UnityEvent<NpcContainer> FightWindowWaitingForCombo = new();
        public static readonly UnityEvent<NpcContainer> FightWindowCombo = new();

        public static readonly UnityEvent<NpcContainer, NpcContainer> SetHeroAsTarget = new();

        /// <summary>
        /// An attribute was written via NpcService.SetAttribute or synced from the instance (SyncHeroInstanceToVob).
        /// </summary>
        public static readonly UnityEvent<NpcContainer, NpcAttribute> NpcAttributeChanged = new();

        /// <summary>
        /// NpcContainer - who attacks
        /// NpcContainer - who got hit
        /// Vector3      - at which position
        /// </summary>
        public static readonly UnityEvent<NpcContainer, NpcContainer, Vector3> FightHit = new();

        /// <summary>
        /// NpcContainer - caster
        /// NpcContainer - target
        /// Vector3      - hit position
        /// int          - total spell damage (SPL_DAMAGE_* * level, already calculated)
        /// </summary>
        public static readonly UnityEvent<NpcContainer, NpcContainer, Vector3, int> SpellHit = new();

        /// <summary>
        /// A spread spell (SpellConst.SpreadEffectNames, fire storm) landed on the ground/a wall - its burning area
        /// damages the NPCs around.
        /// NpcContainer - caster
        /// Vector3      - impact position
        /// int          - total spell damage
        /// </summary>
        public static readonly UnityEvent<NpcContainer, Vector3, int> SpellSpreadHit = new();

        /// <summary>
        /// The game time jumped (hero slept - PC_Sleep's Wld_SetTime, Marvin's time skip).
        /// </summary>
        public static readonly UnityEvent GameTimeSkipped = new();
        // NPC landed after a fall higher than FALLDOWN_HEIGHT (damage already minus PROT_FALL).
        public static readonly UnityEvent<NpcContainer, int> FallDamage = new();

        /// <summary>
        /// Arrow/bolt hit (VR crossbow/bow projectiles).
        /// NpcContainer - shooter
        /// NpcContainer - target
        /// Vector3      - hit position
        /// (weapon, damageScale) - the ranged weapon (damage, damage type) + scale (VR bow: partly drawn string),
        ///                         1 = engine damage. A tuple: UnityEvent takes at most 4 arguments.
        /// </summary>
        public static readonly UnityEvent<NpcContainer, NpcContainer, Vector3, (ZenKit.Daedalus.ItemInstance weapon, float damageScale)> RangedHit = new();

        /// <summary>
        /// NpcContainer - who performs the finishing move (attacker)
        /// NpcContainer - who is executed (unconscious target)
        /// </summary>
        public static readonly UnityEvent<NpcContainer, NpcContainer> FightFinishingMove = new();

        /// <summary>
        /// NpcContainer - the hero, who just got knocked out (BodyState is already BsUnconscious)
        /// </summary>
        public static readonly UnityEvent<NpcContainer> HeroKnockedOut = new();

        /// <summary>
        /// Daedalus Npc_RemoveInvItem(s) only (not our own VR inventory syncs).
        /// NpcContainer - whose inventory lost the item
        /// int - item instance index
        /// int - amount
        /// </summary>
        public static readonly UnityEvent<NpcContainer, int, int> ScriptRemovedInvItems = new();

        /// <summary>
        /// An inventory view (NPC loot sockets, NPC backpack, trade counter) changed an NPC's items - the other views
        /// showing the same NPC refresh.
        /// NpcContainer - whose inventory changed
        /// </summary>
        public static readonly UnityEvent<NpcContainer> NpcInventoryChanged = new();

        /// <summary>
        /// Trade with an NPC (TradeService). TradeSession - the running trade.
        /// </summary>
        public static readonly UnityEvent<Gothic.Core.Models.Trade.TradeSession> TradeStarted = new();
        public static readonly UnityEvent<Gothic.Core.Models.Trade.TradeSession> TradeOfferChanged = new();
        public static readonly UnityEvent<Gothic.Core.Models.Trade.TradeSession> TradeCommitted = new();
        public static readonly UnityEvent<Gothic.Core.Models.Trade.TradeSession> TradeClosed = new();

        /// <summary>
        /// PlayVideo/PlayVideoEx from Daedalus (chapter videos, G1 ending "Extro.bik"). string = video file name.
        /// </summary>
        public static readonly UnityEvent<string> ScriptPlayVideo = new();
        // ExitGame (G1 ending) / ExitSession (G2 ending): the game ends after the queued videos (credits).
        public static readonly UnityEvent ScriptExitGame = new();
        // Mdl_ApplyOverlayMDSTimed on the hero (overlay name, seconds) - e.g. speed potions (HUMANS_SPRINT.MDS).
        public static readonly UnityEvent<string, float> ScriptHeroOverlayTimed = new();


        // LockPicking events
        // 1. VobContainer -> LockPick
        // 2. VobContainer -> Door/Chest
        // 3. HandSide (for VR) -> (0 = Left, 1 = Right)
        public static readonly UnityEvent<VobContainer, VobContainer, int> LockPickComboCorrect = new();
        public static readonly UnityEvent<VobContainer, VobContainer, int> LockPickComboWrong = new();
        public static readonly UnityEvent<VobContainer, VobContainer, int> LockPickComboFinished = new();

        // FIXME - If LockPick in hand is Amount=0, then destroy as Mesh.
        public static readonly UnityEvent<VobContainer, VobContainer, int> LockPickComboBroken = new();

        // DocModel — document to show; GameObject — item GO to attach the viewer to (may be null in flat mode).
        public static readonly UnityEvent<DocModel, GameObject> DocShow = new();
        // Text on screen for some seconds (Daedalus PrintScreen()/AI_PrintScreen()/Print() or engine messages like
        // Marvin's "testmode on").
        public static readonly UnityEvent<PrintScreenMessage> PrintScreen = new();

        // Game menu (MenuHandler): a (sub)menu got opened - also when switching between submenus. GameObject is the
        // opened submenu.
        public static readonly UnityEvent<GameObject> MenuOpened = new();
        // Game menu (MenuHandler) got closed completely.
        public static readonly UnityEvent MenuClosed = new();

        // Marvin: the list of recently executed commands changed.
        public static readonly UnityEvent MarvinRecentCommandsChanged = new();
        // Marvin mode got activated (true) or deactivated (false).
        public static readonly UnityEvent<bool> MarvinModeChanged = new();
        // The Gothic-Unity logo in the menu got clicked. VR uses the event data to vibrate the clicking hand.
        public static readonly UnityEvent<PointerEventData> MarvinLogoPoked = new();
    }
}
