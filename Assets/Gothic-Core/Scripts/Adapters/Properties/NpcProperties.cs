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
        // DeveloperConfig.EnableRingItemSlots: the hero's favorite (not equippable) items in the VR favorites ring -
        // Daedalus symbol names, saved in UNITYSAVE.json.
        public List<string> FavoriteItems = new();
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
        // Stores the overlay set by Daedalus routines (Mdl_ApplyOverlayMds). DrawWeapon temporarily
        // replaces MdsNameOverlay with a combat overlay; UndrawWeapon restores from this.
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
        // DeveloperConfig.EnableNpcPrehitReaction: until when (Time.time) this NPC - the hero - counts as starting a
        // melee hit (the engine's isPrehit(): an attack animation before its hit frame).
        public float MeleePrehitUntil = float.MinValue;
        // The material of the weapon swung last (ItemMaterial, -1 = fists) - the hero's held weapon isn't equipped in VR.
        public int MeleeSwingMaterial = -1;
        // DeveloperConfig.EnableEngineMeleeDamage: the weapon of the hero's VR hit (set only during the FightHit event).
        public ItemInstance MeleeHitWeapon;
        // DeveloperConfig.EnableVrHeroBlock: the hero parries (VR: both hands on a melee weapon, both triggers held) -
        // the held weapon's material and the direction he faces (head) while he does.
        public bool IsBlocking;
        public int BlockWeaponMaterial = -1;
        public UnityEngine.Vector3 BlockForward;
        // AI_LookAtNpc target until AI_StopLookAt (Npc_GetLookAtTarget).
        public NpcInstance LookAtTarget;

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

        // Time.time when this NPC gave up chasing the hero from far away (DeveloperConfig.EnableChaseGiveUpCooldown).
        public float HeroChaseGivenUpTime = float.MinValue;
        // Time.time of the last threat before a chase (DeveloperConfig.EnablePursuitWarn).
        public float LastPursuitWarnTime = float.MinValue;
        public LoopState CurrentLoopState = LoopState.None;

        // DeveloperConfig.EnableNpcComboLimit: attacks chained at combo windows since the last one that played to its end.
        public int AttackComboLength;
        // DeveloperConfig.EnableNpcPhysicalProjectiles: AI_Attack turns in a row without a free line of shot.
        public int BlockedShotCount;
        // DeveloperConfig.EnableEngineNpcCombo: the attack animation that keeps playing for the next attack to continue,
        // and the combo part it continues with.
        public string ComboContinueAnimation;
        public int ComboContinueIndex;
        public AbstractAnimationAction CurrentAction;

        // "other" and "victim" saved when a state is started via AI_StartState with a specific enemy.
        // Used by AiHandler.Update() so state functions (ZS_*_Start/Loop) receive the correct NPC,
        // not the hero default. Cleared when returning to daily routine.
        public NpcInstance StateOther;
        public NpcInstance StateVictim;

        public VmGothicEnums.Guild TrueGuild;
        public float RefuseTalkTimer;
    }
}
