using System;
using Gothic.Core.Adapters.Npc;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Context;
using Gothic.Core.Models.Doc;
using UnityEngine;
using UnityEngine.Events;
using ZenKit;
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
        /// Daedalus PrintScreen/AI_PrintScreen ("New log entry", "1 item received", ...).
        /// string - text
        /// int - posY in percent of the screen (-1 = centered)
        /// int - seconds to show
        /// </summary>
        public static readonly UnityEvent<string, int, int> ScriptPrintScreen = new();

        /// <summary>
        /// Daedalus Snd_Play - a non-positional (2D) sound like "LogEntry".
        /// </summary>
        public static readonly UnityEvent<string> ScriptSoundPlay = new();


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
    }
}
