using Gothic.Core.Models.Container;

namespace Gothic.Core.Extensions
{
    public static class NpcContainerExtension
    {
        /// <summary>
        /// The hero: his prefab is marked as the hero. Not by Id - monsters have Id 0 like the hero.
        /// </summary>
        public static bool IsHero(this ZenKit.Daedalus.NpcInstance npc)
        {
            var container = npc.GetUserData();
            return container?.PrefabProps != null && container.PrefabProps.IsHero();
        }

        /// <summary>
        /// The NPC reached its mob and is in one of its states (e.g. seated on a bench).
        /// </summary>
        public static bool IsUsingMob(this NpcContainer npc)
        {
            return npc.PrefabProps?.CurrentInteractable != null && npc.Props.CurrentInteractableStateId >= 0;
        }
    }
}
