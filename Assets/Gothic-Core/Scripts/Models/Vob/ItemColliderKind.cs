namespace Gothic.Core.Models.Vob
{
    /// <summary>
    /// Which collider strategy an item's mesh gets (VobItemColliderBuilder). Decided from the item's flags
    /// (ItemInstanceExtension.GetColliderKind), never from its mesh name - a mod bow can be called anything.
    /// Melee weapons are fitted while pre-caching (boxes on blade, guard, grip), at runtime they are sliced like
    /// Default.
    /// </summary>
    public enum ItemColliderKind
    {
        Default,
        Melee,
        Bow,
        Crossbow
    }
}
