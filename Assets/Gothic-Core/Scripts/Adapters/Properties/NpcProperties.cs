using System;
using System.Collections.Generic;
using Gothic.Core.Domain.Npc.Actions.AnimationActions;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Npc;
using Gothic.Core.Models.Vm;
using Gothic.Core.Models.Vob.WayNet;
using ZenKit.Daedalus;

namespace Gothic.Core.Adapters.Properties
{
    public class NpcProperties
    {
        public ZenKit.Vobs.Npc NpcVob = new ();
        
        // Misc
        public List<InfoInstance> Dialogs = new();

        // Routines
        public List<RoutineData> Routines = new();
        public RoutineData RoutinePrevious;
        public RoutineData RoutineCurrent;

        // WayNet
        public WayPoint CurrentWayPoint;
        public FreePoint CurrentFreePoint;

        // NPC items/talents/...
        public List<ItemInstance> EquippedItems = new();
        public string UsedItemSlot;
        public bool HasItemEquipped;
        public int CurrentItem = -1;
        // We need to start with an "invalid" value as >0< is an allowed state value like in >t_Potion_Stand_2_S0<
        public int ItemAnimationState = -1;

        public int CurrentInteractableStateId = -1;

        // Visual
        public string MdmName;
        public string MdsNameBase;
        public string MdsNameOverlay;
        public string[] MdsNames => new[] { MdsNameBase, MdsNameOverlay };

        // An MDS file has always an MDH file named identically
        public string MdhNameBase => MdsNameBase;
        public string MdhNameOverlay => MdsNameOverlay;

        public ExtSetVisualBodyData BodyData;

        // Perceptions
        public Dictionary<VmGothicEnums.PerceptionType, int> Perceptions = new();
        public float PerceptionTime = 5f; // Default in seconds
        public float CurrentPerceptionTime;

        // AI topics

        public NpcInstance EnemyNpc;
        public NpcInstance TargetNpc;
        // AI_LookAtNpc target until AI_StopLookAt (Npc_GetLookAtTarget).
        public NpcInstance LookAtTarget;
        // Talked to the hero (Npc_SetKnowsPlayer in G1's ZS_Talk). Not saved: gone with the NPC on load/new game.
        public bool IsKnowingHero;

        public enum LoopState
        {
            None,
            Start,
            Loop,
            End,
            AfterEnd
        }
        public readonly Queue<AbstractAnimationAction> AnimationQueue = new();

        // Mdl_ApplyRandomAni: a state animation -> the animations played now and then while it runs
        // (S_EAT -> R_ROAM1, R_ROAM2). Mdl_ApplyRandomAniFreq: the average seconds between them.
        public readonly Dictionary<string, List<string>> RandomAnis = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<string, float> RandomAniFrequencies = new(StringComparer.OrdinalIgnoreCase);
        public float NextRandomAniTime;

        // HINT: This information isn't set within Daedalus. We need to define it manually.
        // HINT: i.e. every animation might have a BS. E.g. when AI_TakeItem() is called, we set BS.BS_TAKEITEM
        public VmGothicEnums.BodyState BodyState;

        /// <summary>
        /// Npc_IsDead(). Like Gothic, it's the death transition (FightService, loading a dead NPC), not HP <= 0:
        /// scripts set HP directly and prepare NPCs with HP 0.
        /// </summary>
        public bool IsDead => BodyState == VmGothicEnums.BodyState.BsDead;

        /// <summary>
        /// Knocked out (BS_UNCONSCIOUS), like the scripts' C_NpcIsDown (ZS_Unconscious, ZS_MagicSleep).
        /// </summary>
        public bool IsKnockedOut => BodyState == VmGothicEnums.BodyState.BsUnconscious;

        /// <summary>
        /// Dead or knocked out.
        /// </summary>
        public bool IsDown => IsDead || IsKnockedOut;

        // StateStart is fetched from ZenKit.Vobs.Npc.CurrentStateIndex
        public int StateLoop;
        public int StateEnd;

        // State time is activated within AI_StartState()
        // e.g. used to handle random wait loops for idle eating animations (eat a cheese only every n-m seconds)
        public bool IsStateTimeActive;
        public float StateTime;
        public LoopState CurrentLoopState = LoopState.None;
        public AbstractAnimationAction CurrentAction;

        // "other" and "victim" saved when a state is started via AI_StartState with a specific enemy.
        // Used by AiHandler.Update() so state functions (ZS_*_Start/Loop) receive the correct NPC,
        // not the hero default. Cleared when returning to daily routine.
        public NpcInstance StateOther;
        public NpcInstance StateVictim;

        // Wld_DetectItem's world item - the script's 'item' is only the instance data (AI_GotoItem, Npc_GetDistToItem).
        public VobContainer DetectedItem;

        // The current state's _End function is running (AI_StartState from inside it must not run it again).
        public bool IsInStateEnd;
        
        public VmGothicEnums.Guild TrueGuild;
        public float RefuseTalkTimer;
    }
}
