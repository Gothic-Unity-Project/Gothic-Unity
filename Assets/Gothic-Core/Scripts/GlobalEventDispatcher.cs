using System;
using Gothic.Core.Adapters.Npc;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Context;
using Gothic.Core.Models.Doc;
using Gothic.Core.Models.Trade;
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

        // AI_Teleport moves the hero (a teleport spell) - VR: the held items go into the backpack first.
        public static readonly UnityEvent HeroScriptTeleporting = new();

        public static readonly UnityEvent<DateTime> GameTimeSecondChangeCallback = new();
        public static readonly UnityEvent<DateTime> GameTimeMinuteChangeCallback = new();
        public static readonly UnityEvent<DateTime> GameTimeHourChangeCallback = new();

        public static readonly UnityEvent<GameObject> MusicZoneEntered = new();
        public static readonly UnityEvent<GameObject> MusicZoneExited = new();
        public static readonly UnityEvent<string, string> LevelChangeTriggered = new();
        
        public static readonly UnityEvent GothicInisInitialized = new();
        public static readonly UnityEvent<string, object> PlayerPrefUpdated = new();
        
        // The running game is replaced (new game or loaded save). VM, hero and world still exist. Called before
        // LoadGameStart, which creates the new VM.
        public static readonly UnityEvent UnloadGameStart = new();
        public static readonly UnityEvent LoadGameStart = new();

        // The current world's runtime state is stored into its world data: before a save game is written (true) and
        // before we leave the world (false). E.g. VR decides which items are part of the world or inventory.
        public static readonly UnityEvent<bool> StoreWorldStateStart = new();
        
        
        public static readonly UnityEvent<NpcContainer, NpcLoader, bool, bool> NpcMeshCullingChanged = new();
        public static readonly UnityEvent<GameObject> VobMeshCullingChanged = new();

        public static readonly UnityEvent<INpc> CreateNpc = new();

        /// <summary>
        /// An item was put into the loaded world (Wld_InsertItem) - it lies there like the world's own items.
        /// Raised by VobService.ExtWldInsertItem.
        /// </summary>
        public static readonly UnityEvent<IVirtualObject> WorldItemInserted = new();

        /// <summary>
        /// Plays a Bink video in the platform's cinema (VR: VRCinema). Several calls in a row are queued.
        /// Sources: PlayVideo/PlayVideoEx from Daedalus (chapter videos, G1 ending "Extro.bik") and the logo videos.
        /// string = video file name or path.
        /// </summary>
        public static readonly UnityEvent<string> PlayVideo = new();

        /// <summary>
        /// All queued videos are played (or skipped) and the cinema is closed again.
        /// </summary>
        public static readonly UnityEvent VideoPlaybackFinished = new();

        
        // Fight events

        /// <summary>
        /// HINT: Left/Right have other values than Gothic.VR.Interaction.Models.VrHandSide (Left=0, Right=1).
        /// Map them with VrHandSideExtensions, never cast.
        /// </summary>
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
        /// The hero's mana bar fades in (true) or out (false) - VR: while a rune or scroll is readied (VRRuneCaster).
        /// </summary>
        public static readonly UnityEvent<bool> HeroManaBarRequested = new();

        /// <summary>
        /// NpcContainer - who attacks
        /// NpcContainer - who got hit
        /// Vector3      - at which position
        /// ItemInstance - the weapon the hero hit with (VR: the one in his hand - it isn't equipped), null for fists.
        ///                NPC hits pass null - their damage comes from their equipped weapon.
        /// </summary>
        public static readonly UnityEvent<NpcContainer, NpcContainer, Vector3, ItemInstance> FightHit = new();

        /// <summary>
        /// The hero starts a melee swing (VR: the held weapon got faster than its attack threshold) - the engine's
        /// "prehit" of an attack animation. NPCs fighting him can parry or jump back (FightService).
        /// </summary>
        public static readonly UnityEvent HeroMeleeSwing = new();

        /// <summary>
        /// The blocking hero parried a hit.
        /// NpcContainer - the attacker
        /// </summary>
        public static readonly UnityEvent<NpcContainer> HeroBlockedHit = new();

        /// <summary>
        /// The hero started blocking (VR: both hands on the same melee weapon, both triggers held).
        /// </summary>
        public static readonly UnityEvent HeroBlockStarted = new();

        /// <summary>
        /// A melee hit that landed (not parried, blocked, in a dialog, ...) - blood.
        /// NpcContainer - who attacks
        /// NpcContainer - who got hit
        /// Vector3      - at which position
        /// </summary>
        public static readonly UnityEvent<NpcContainer, NpcContainer, Vector3> MeleeHitLanded = new();

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

        // A fire hit set the NPC on fire (or keeps it burning longer) - NpcBurnService plays it.
        public static readonly UnityEvent<NpcContainer> NpcIgnited = new();
        // NPC landed after a fall higher than FALLDOWN_HEIGHT (damage already minus PROT_FALL).
        public static readonly UnityEvent<NpcContainer, int> FallDamage = new();
        // Marvin's "kill": the NPC dies at once, no killer (no experience).
        public static readonly UnityEvent<NpcContainer> CheatKill = new();

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
        /// NpcContainer - lost its last hit points outside of a melee hit (NpcService.ReduceHitPoints: a script's
        /// Npc_ChangeAttribute, drowning, a damage zone). FightService decides how it goes down.
        /// </summary>
        public static readonly UnityEvent<NpcContainer> HitPointsDepleted = new();

        /// <summary>
        /// NpcContainer - the hero, who just got knocked out (BodyState is already BsUnconscious). He lies for
        /// FightConst.HeroKnockoutRecoverySeconds. The VR knockout view (veil, heartbeat) listens to it.
        /// </summary>
        public static readonly UnityEvent<NpcContainer> HeroKnockedOut = new();

        /// <summary>
        /// The knocked out hero got up again (FightService). Not raised when a game is loaded meanwhile
        /// (LoadGameStart ends the knockout).
        /// </summary>
        public static readonly UnityEvent HeroRecovered = new();

        /// <summary>
        /// The hero died (FightService), after his death state started. He stays dead until a game is loaded.
        /// NpcContainer - who killed him (the hero himself for a script)
        /// </summary>
        public static readonly UnityEvent<NpcContainer> HeroDied = new();

        /// <summary>
        /// A sleep spell put the hero to sleep (HeroSpellStateDomain, ZS_MagicSleep): he lies until HeroWokeUp. The VR
        /// hero body lies down like knocked out (T_STAND_2_VICTIM_SLE, S_VICTIM_SLE).
        /// NpcContainer - the hero
        /// </summary>
        public static readonly UnityEvent<NpcContainer> HeroFellAsleep = new();

        /// <summary>
        /// The sleeping hero woke up (time over, damage, another spell state, the hero gone). Not raised when a game
        /// is loaded meanwhile (LoadGameStart ends the sleep).
        /// </summary>
        public static readonly UnityEvent HeroWokeUp = new();

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

        // ExitGame (G1 ending) / ExitSession (G2 ending): the game ends after the queued videos (credits).
        public static readonly UnityEvent ScriptExitGame = new();
        // Mdl_ApplyOverlayMDSTimed on the hero (overlay name, seconds) - e.g. speed potions (HUMANS_SPRINT.MDS).
        public static readonly UnityEvent<string, float> ScriptHeroOverlayTimed = new();

        /// <summary>
        /// The NPC's body visual (Mdl_SetVisualBody: body, textures, armor) was set (NpcService.ExtSetVisualBody).
        /// The VR hero body rebuilds itself on it.
        /// </summary>
        public static readonly UnityEvent<NpcContainer> NpcVisualBodyChanged = new();


        // LockPicking events
        // 1. VobContainer -> LockPick
        // 2. VobContainer -> Door/Chest
        // 3. HandSide (for VR) -> int of Gothic.VR.Interaction.Models.VrHandSide (0 = Left, 1 = Right),
        //    not GlobalEventDispatcher.HandSide.
        public static readonly UnityEvent<VobContainer, VobContainer, int> LockPickComboCorrect = new();
        public static readonly UnityEvent<VobContainer, VobContainer, int> LockPickComboWrong = new();
        public static readonly UnityEvent<VobContainer, VobContainer, int> LockPickComboFinished = new();

        // FIXME - If LockPick in hand is Amount=0, then destroy as Mesh.
        public static readonly UnityEvent<VobContainer, VobContainer, int> LockPickComboBroken = new();

        // DocModel — document to show; GameObject — item GO to attach the viewer to (may be null in flat mode).
        public static readonly UnityEvent<DocModel, GameObject> DocShow = new();

        // Physical hands (VR): a hand or the item it holds hit something. Once per impact (cooldown and sliding are
        // filtered by VrPhysicsHand).
        // 1. HandSide -> int of Gothic.VR.Interaction.Models.VrHandSide (0 = Left, 1 = Right)
        // 2. Impact speed in m/s along the contact normal
        public static readonly UnityEvent<int, float> PhysicalHandContact = new();

        // Body slots (VR): a hand reached a slot on the body it can use - with something the slot takes, or empty at
        // the slot's content (backpack on the shoulder, holstered item). Raised by VrBodySlot.
        // 1. HandSide -> int of Gothic.VR.Interaction.Models.VrHandSide (0 = Left, 1 = Right)
        public static readonly UnityEvent<int> BodySlotReached = new();

        // Ledge climbing (VR): a hand came into touch range of a climbable lip (VrLedgeReach, hints not Off).
        // 1. HandSide -> int of Gothic.VR.Interaction.Models.VrHandSide (0 = Left, 1 = Right)
        public static readonly UnityEvent<int> LedgeTouched = new();

        // Ledge climbing (VR): a comfort climb found no place on top to put the player (VrLedgeClimb).
        // 1. HandSide -> int of Gothic.VR.Interaction.Models.VrHandSide (0 = Left, 1 = Right)
        public static readonly UnityEvent<int> LedgeClimbRefused = new();

        // Fists (VR): a fist got faster than a punch needs - once per swing (VrFistDomain).
        // 1. HandSide -> int of Gothic.VR.Interaction.Models.VrHandSide (0 = Left, 1 = Right)
        public static readonly UnityEvent<int> FistSwung = new();

        // Fists (VR): a punch hit an NPC - raised right before its FightHit (VrFistDomain).
        // 1. HandSide -> int of Gothic.VR.Interaction.Models.VrHandSide (0 = Left, 1 = Right)
        public static readonly UnityEvent<int> FistPunchLanded = new();

        // Stab finish (VR): the blade got thrust into an unconscious NPC and sticks now (VrStabFinishDomain).
        // 1. HandSide -> int of Gothic.VR.Interaction.Models.VrHandSide (0 = Left, 1 = Right)
        public static readonly UnityEvent<int> StabStuck = new();

        // Stab finish (VR): the blade is still in the body - once per StabHoldPulseSeconds until the finish.
        // 1. HandSide -> int of Gothic.VR.Interaction.Models.VrHandSide (0 = Left, 1 = Right)
        public static readonly UnityEvent<int> StabHeld = new();

        // Stab finish (VR): the blade was held in long enough - raised right before its FightFinishingMove.
        // 1. HandSide -> int of Gothic.VR.Interaction.Models.VrHandSide (0 = Left, 1 = Right)
        public static readonly UnityEvent<int> StabFinished = new();

        // Backpack (VR): the backpack is in a hand now (true) or in none any more (false). Raised by VRBackpack.
        public static readonly UnityEvent<bool> BackpackInHandChanged = new();

        // Text on screen for some seconds (Daedalus PrintScreen()/AI_PrintScreen()/Print() or engine messages like
        // Marvin's "testmode on").
        public static readonly UnityEvent<PrintScreenMessage> PrintScreen = new();

        // Game menu (MenuHandler): a (sub)menu got opened - also when switching between submenus. GameObject is the
        // opened submenu.
        public static readonly UnityEvent<GameObject> MenuOpened = new();

        // Trading (TradeService) started with this NPC: the VR trade counter opens, the dialog goes on.
        public static readonly UnityEvent<NpcContainer> TradeStarted = new();
        // Trading with this NPC ended (closed, the dialog ended, the world left) - the offers went back.
        public static readonly UnityEvent<NpcContainer> TradeEnded = new();
        // The counter's offers changed (TradeService.Current).
        public static readonly UnityEvent<TradeSession> TradeOfferChanged = new();
        // The counter's offers changed hands (TryCommit), the offers are empty again.
        public static readonly UnityEvent<TradeSession> TradeCommitted = new();
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
