using System;
using Gothic.Core.Adapters.Npc;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Context;
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
        /// NpcContainer - who attacks
        /// NpcContainer - who got hit
        /// Vector3      - at which position
        /// </summary>
        public static readonly UnityEvent<NpcContainer, NpcContainer, Vector3> FightHit = new();
        

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
