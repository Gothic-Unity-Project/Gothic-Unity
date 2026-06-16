namespace Gothic.Core.Adapters.Npc
{
    public interface INpcSubtitles
    {
        void ShowSubtitles(string text);
        void HideSubtitles();
        void ScheduleHide(float delay);
    }
}
