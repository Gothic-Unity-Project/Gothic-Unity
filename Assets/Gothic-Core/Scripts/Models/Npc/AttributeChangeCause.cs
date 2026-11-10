namespace Gothic.Core.Models.Npc
{
    /// <summary>
    /// Why an NPC attribute was written (NpcService.SetAttribute). Listeners decide what to show from it, e.g. hit numbers.
    /// </summary>
    public enum AttributeChangeCause
    {
        Default,
        Knockout,
        Charging
    }
}
