using System;
using Gothic.Core.Adapters.Npc;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Context;
using UnityEngine;
using UnityEngine.Events;
using ZenKit;
using ZenKit.Vobs;

namespace Gothic.Core
{
    /// <summary>
    /// Loading/Unloading order of scenes:
    /// https://github.com/Gothic-UnZENity-Project/Gothic-UnZENity/blob/main/Docs/development/diagrams/SceneLoading.drawio.png
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
        /// NpcContainer - who attacks
        /// NpcContainer - who got hit
        /// Vector3      - at which position
        /// </summary>
        public static readonly UnityEvent<NpcContainer, NpcContainer, Vector3> FightHit = new();
        

        // LockPicking events
        // 1. VobContainer -> LockPick
        // 2. VobContainer -> Door/Chest
        // 3. HandSide (for VR) -> (0 = Left, 1 = Right)
        public static readonly UnityEvent<VobContainer, VobContainer, int> LockPickComboCorrect = new();
        public static readonly UnityEvent<VobContainer, VobContainer, int> LockPickComboWrong = new();
        public static readonly UnityEvent<VobContainer, VobContainer, int> LockPickComboFinished = new();

        // FIXME - If LockPick in hand is Amount=0, then destroy as Mesh.
        public static readonly UnityEvent<VobContainer, VobContainer, int> LockPickComboBroken = new();
    }
}
