using System.Collections.Generic;
using Gothic.Core.Domain.Npc.Actions.AnimationActions;
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
        // The overlay of the scripts (Mdl_ApplyOverlayMds). A drawn weapon replaces MdsNameOverlay with the fight
        // overlay of the NPC's talent (HUMANS_1HST1.MDS), putting it away brings this one back.
        public string MdsNameRoutineOverlay;
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

        // Like the engine, a state keeps the other/victim it was started with (AI_StartState): ZS_Attack_Loop fights
        // the wolf it was started against, not the hero. Cleared when the NPC goes back to its routine.
        public NpcInstance StateOther;
        public NpcInstance StateVictim;

        // Fight combos (AttackPlayAni): attacks chained in combo windows since the last one that played to its end,
        // and the attack animation that keeps playing for the next attack to continue with its part.
        public int AttackComboLength;
        public string ComboContinueAnimation;
        public int ComboContinueIndex;
        // The weapon of the hero's VR hit, set only during its FightHit event: the held weapon isn't equipped in VR.
        public ItemInstance MeleeHitWeapon;

        public enum LoopState
        {
            None,
            Start,
            Loop,
            End,
            AfterEnd
        }
        public readonly Queue<AbstractAnimationAction> AnimationQueue = new();

        // HINT: This information isn't set within Daedalus. We need to define it manually.
        // HINT: i.e. every animation might have a BS. E.g. when AI_TakeItem() is called, we set BS.BS_TAKEITEM
        public VmGothicEnums.BodyState BodyState;

        // StateStart is fetched from ZenKit.Vobs.Npc.CurrentStateIndex
        public int StateLoop;
        public int StateEnd;

        // State time is activated within AI_StartState()
        // e.g. used to handle random wait loops for idle eating animations (eat a cheese only every n-m seconds)
        public bool IsStateTimeActive;
        public float StateTime;
        public LoopState CurrentLoopState = LoopState.None;
        public AbstractAnimationAction CurrentAction;
        
        public VmGothicEnums.Guild TrueGuild;
        public float RefuseTalkTimer;
    }
}
