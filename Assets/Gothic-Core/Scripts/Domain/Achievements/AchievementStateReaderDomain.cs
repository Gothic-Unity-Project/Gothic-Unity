using Gothic.Core.Extensions;
using Gothic.Core.Manager;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.World;
using Reflex.Attributes;

namespace Gothic.Core.Domain.Achievements
{
    /// <summary>
    /// Reads the game state for conditions from the scripts' VM, the quest log, the told infos and the hero's packed
    /// inventory. Called only at the moment of a fact or a variable check, never per frame.
    /// </summary>
    public class AchievementStateReaderDomain : IAchievementStateReader
    {
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly StoryService _storyService;
        [Inject] private readonly DialogService _dialogService;
        [Inject] private readonly NpcAiService _npcAiService;
        [Inject] private readonly NpcInventoryService _npcInventoryService;
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;


        public int GetVariable(int symbolIndex)
        {
            return _gameStateService.GothicVm?.GetSymbolByIndex(symbolIndex)?.GetInt(0) ?? 0;
        }

        public int GetLogTopicStatus(string topic)
        {
            return _storyService.GetLogTopicStatus(topic);
        }

        public bool IsInfoTold(int infoIndex)
        {
            return _dialogService.IsInfoTold(infoIndex);
        }

        public int GetHeroTrueGuild()
        {
            var hero = _gameStateService.GothicVm.GetHeroContainer();
            return hero?.Instance == null ? -1 : _npcAiService.ExtGetTrueGuild(hero.Instance);
        }

        public int GetHeroItemCount(int itemIndex)
        {
            var hero = _gameStateService.GothicVm.GetHeroContainer();
            return hero?.Instance == null ? 0 : _npcInventoryService.ExtNpcHasItems(hero.Instance, itemIndex);
        }

        public bool TryGetNpcAivar(int npcInstanceIndex, int aivarIndex, out int value)
        {
            foreach (var npc in _multiTypeCacheService.NpcCache)
            {
                if (npc.Instance == null || npc.Instance.Index != npcInstanceIndex)
                    continue;

                value = npc.Instance.GetAiVar(aivarIndex);
                return true;
            }

            value = 0;
            return false;
        }
    }
}
