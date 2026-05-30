using System.Collections.Generic;
using GUZ.Core.Models.Npc;
using GUZ.Core.Models.Vm;
using GUZ.Core.Models.Vob.WayNet;
using GUZ.Core.Domain.Npc.Actions.AnimationActions;
using ZenKit.Daedalus;

namespace GUZ.Core.Adapters.Properties
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
