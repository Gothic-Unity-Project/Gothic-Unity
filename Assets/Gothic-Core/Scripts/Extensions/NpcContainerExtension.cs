using System;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using UnityEngine;

namespace Gothic.Core.Extensions
{
    public static class NpcContainerExtension
    {
        /// <summary>
        /// The NPC reached its mob and is in one of its states (e.g. seated on a bench).
        /// </summary>
        public static bool IsUsingMob(this NpcContainer npc)
        {
            return npc.PrefabProps?.CurrentInteractable != null && npc.Props.CurrentInteractableStateId >= 0;
        }

        public static bool IsHero(this NpcContainer npc)
        {
            return npc?.PrefabProps != null && npc.PrefabProps.IsHero();
        }

        /// <summary>
        /// A true human like the scripts' C_NpcIsHuman (guild below GIL_SEPERATOR_HUM): orcs and monsters aren't.
        /// </summary>
        public static bool IsHuman(this NpcContainer npc)
        {
            return npc.Instance.Guild < (int)VmGothicEnums.Guild.GIL_SEPERATOR_HUM;
        }

        /// <summary>
        /// Knocked out (BS_UNCONSCIOUS). ZS_MagicFreeze sets the same body state, but like the scripts' C_NpcIsDown a
        /// frozen NPC isn't down: fighters would leave it alone and the next hit wouldn't kill it.
        /// </summary>
        public static bool IsKnockedOut(this NpcContainer npc)
        {
            return npc?.Props != null && npc.Props.BodyState == VmGothicEnums.BodyState.BsUnconscious &&
                   !npc.IsMagicFrozen();
        }

        /// <summary>
        /// Dead or knocked out.
        /// </summary>
        public static bool IsDown(this NpcContainer npc)
        {
            return npc?.Props != null && (npc.Props.BodyState == VmGothicEnums.BodyState.BsDead || npc.IsKnockedOut());
        }

        public static bool IsMagicFrozen(this NpcContainer npc)
        {
            var state = npc?.Vob?.CurrentStateName;
            return state != null && state.StartsWith("ZS_MAGICFREEZE", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The newest of the scripts' overlays (Mdl_ApplyOverlayMds), null without one.
        /// </summary>
        public static string GetScriptOverlay(this NpcContainer npc)
        {
            var overlays = npc.Vob.Overlays;
            return overlays.Count > 0 ? overlays[overlays.Count - 1] : null;
        }

        /// <summary>
        /// Where the NPC stands for a fight. The VR hero's GameObject is the rig: he stands where his head is, at the
        /// rig's floor height.
        /// </summary>
        public static Vector3 GetFightPosition(this NpcContainer npc)
        {
            var position = npc.Go.transform.position;
            var head = npc.IsHero() ? npc.PrefabProps.Head : null;
            return head == null ? position : new Vector3(head.position.x, position.y, head.position.z);
        }
    }
}
