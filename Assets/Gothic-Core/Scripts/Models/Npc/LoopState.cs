namespace Gothic.Core.Models.Npc
{
    /// <summary>
    /// Where an NPC is in its current AI state (ZS_*): its _Start, _Loop or _End function.
    /// </summary>
    public enum LoopState
    {
        None,
        Start,
        Loop,
        End,
        AfterEnd
    }
}
