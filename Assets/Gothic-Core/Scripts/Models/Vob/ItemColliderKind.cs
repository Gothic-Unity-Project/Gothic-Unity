namespace Gothic.Core.Models.Vob
{
    /// <summary>
    /// Which collider strategy an item's mesh gets (VobItemColliderBuilder). Decided from the item's flags
    /// (ItemInstanceExtension.GetColliderKind), never from its mesh name - a mod bow can be called anything.
    /// </summary>
    public enum ItemColliderKind
    {
        Default,
        Bow,
        Crossbow
    }
}
