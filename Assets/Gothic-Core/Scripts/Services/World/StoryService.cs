using System.Collections.Generic;
using GUZ.Core.Services.Context;
using Reflex.Attributes;
using ZenKit;

namespace GUZ.Core.Services.World
{
    public class StoryService
    {
        // Chapter switching topics
        public bool _isChapterSwitchPending;
        
        [Inject] private readonly SaveGameService _saveGameService;
        [Inject] private readonly ContextInteractionService _contextInteractionService;
        
        private (string chapter, string text, string texture, string wav, int time) _chapterSwitchData;

        private SaveState _saveState => _saveGameService.Save.State;


        public void ExtLogCreateTopic(string name, SaveTopicSection section)
        {
            // Does entry already exist?
            if (_saveState.LogTopics.Exists(i => i.Description == name))
            {
                return;
            }

            _saveState.AddLogTopic(new SaveLogTopic()
            {
                Description = name,
                Status = SaveTopicStatus.Free,
                Entries = new List<string>(),
                Section = section,
            });
        }

        public void ExtLogSetTopicStatus(string name, SaveTopicStatus status)
        {
            for (var i = 0; i < _saveState.LogTopicCount; i++)
            {
                var topic = _saveState.GetLogTopic(i);

                if (topic.Description == name)
                {
                    topic.Status = status;
                    _saveState.SetLogTopic(i, topic);
                }
            }
        }

        public void ExtLogAddEntry(string topic, string entry)
        {
            for (var i = 0; i < _saveState.LogTopicCount; i++)
            {
                var logTopic = _saveState.GetLogTopic(i);

                if (logTopic.Description == topic)
                {
                    logTopic.Entries.Add(entry);
                    _saveState.SetLogTopic(i, logTopic);
                }
            }
        }

        public void ExtIntroduceChapter(string chapter, string text, string texture, string wav, int time)
        {
            _isChapterSwitchPending = true;
            _chapterSwitchData = (chapter, text, texture, wav, time);
        }

        public List<SaveLogTopic> GetLogTopics(SaveTopicSection section, SaveTopicStatus status)
        {
            var ret = new List<SaveLogTopic>();

            for (var i = 0; i < _saveState.LogTopicCount; i++)
            {
                var topic = _saveState.GetLogTopic(i);

                if (topic.Section == section && topic.Status == status)
                {
                    ret.Add(topic);
                }
            }

            return ret;
        }

        public void SwitchChapterIfPending()
        {
            if (!_isChapterSwitchPending)
            {
                return;
            }

            _contextInteractionService.IntroduceChapter(
                _chapterSwitchData.chapter,
                _chapterSwitchData.text,
                _chapterSwitchData.texture,
                _chapterSwitchData.wav,
                _chapterSwitchData.time);
            
            _isChapterSwitchPending = false;
            _chapterSwitchData = default;
        }
    }
}
