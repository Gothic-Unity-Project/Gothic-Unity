using System.Linq;
using GUZ.Core.Adapters.Animations.Morph;
using GUZ.Core.Adapters.Npc;
using GUZ.Core.Models.Container;
using GUZ.Core.Extensions;
using GUZ.Core.Const;
using GUZ.Core.Manager;
using GUZ.Core.Services;
using GUZ.Core.Services.Caches;
using GUZ.Core.Services.Config;
using GUZ.Core.Services.Npc;
using Reflex.Attributes;
using UnityEngine;
using Random = UnityEngine.Random;

namespace GUZ.Core.Domain.Npc.Actions.AnimationActions
{
    public class Output : AbstractAnimationAction
    {
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly DialogService _dialogService;
        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;

        protected virtual string OutputName => Action.String0;

        private bool _isHeroSpeaking => Action.Int0 == 0;
        private float _audioPlaySeconds;

        private string _randomDialogAnimationName;


        public Output(AnimationAction action, NpcContainer npcContainer) : base(action, npcContainer)
        { }

        public override void Start()
        {
            if (_dialogService.SkipNextOutput)
            {
                _dialogService.SkipNextOutput = false;
                
                // If - for any reason - the first dialog entry after selecting dialog entry, then we don't skip it.
                if (_isHeroSpeaking)
                {
                    IsFinishedFlag = true;
                    return;
                }
            }
            
            var audioClip = _audioService.CreateAudioClip(OutputName);
            _audioPlaySeconds = audioClip.length;

            // Hero
            if (_isHeroSpeaking)
            {
                _npcService.GetHeroGameObject().GetComponent<AudioSource>().PlayOneShot(audioClip);

                PrintDialog();
            }
            // NPC
            else
            {
                var gestureCount = GetDialogGestureCount();
                var randomId = Random.Range(1, gestureCount + 1);

                _randomDialogAnimationName = $"T_DIALOGGESTURE_{randomId:00}";
                PrefabProps.AnimationSystem.PlayAnimation(_randomDialogAnimationName);
                PrefabProps.AnimationSystem.PlayHeadAnimation(HeadMorph.HeadMorphType.Viseme);

                PrefabProps.NpcSound.PlayOneShot(audioClip);

                PrintDialog();
            }
        }

        private void PrintDialog()
        {
            // FIXME - CutsceneLibrary.Blocks is uncached and will re-read all elements each time we call it! Cache and reuse!
            var currentMessage = _gameStateService.Dialogs.CutsceneLibrary.Blocks.Find(x => x.Name == OutputName).Message;

            if (_isHeroSpeaking)
            {
                _npcService.GetHeroContainer().PrefabProps.NpcSubtitles.ShowSubtitles(currentMessage.Text);
            }
            else
            {
                PrefabProps.NpcSubtitles.ShowSubtitles(currentMessage.Text);
            }
        }

        /// <summary>
        /// Gothic1 and Gothic 2 have different amount of Gestures. As we cached all animation names, we iterate through them once and return its number.
        /// </summary>
        private int GetDialogGestureCount()
        {
            if (_gameStateService.Dialogs.GestureCount == 0)
            {
                // FIXME - We might need to check overlayMds and baseMds
                // FIXME - We might need to save amount of gestures based on mds names (if they differ for e.g. humans and orcs)
                var mds = _resourceCacheService.TryGetModelScript(Props.MdsNameBase);

                _gameStateService.Dialogs.GestureCount = mds.Animations
                    .Count(anim => anim.Name.StartsWithIgnoreCase("T_DIALOGGESTURE_"));
            }

            return _gameStateService.Dialogs.GestureCount;
        }

        public override void StopImmediately()
        {
            _audioPlaySeconds = 0f;

            if (_isHeroSpeaking)
            {
                _npcService.GetHeroGameObject().GetComponent<AudioSource>().Stop();
            }
            // NPC
            else
            {
                PrefabProps.NpcSound.Stop();
                PrefabProps.AnimationSystem.StopAnimation(_randomDialogAnimationName);
            }
        }

        public override bool IsFinished()
        {
            _audioPlaySeconds -= Time.deltaTime;

            if (_audioPlaySeconds <= 0f)
            {
                // Hero
                if (_isHeroSpeaking)
                {
                    _npcService.GetHeroContainer().PrefabProps.NpcSubtitles.HideSubtitles();
                }
                // NPC
                else
                {
                    PrefabProps.AnimationSystem.StopAnimation(_randomDialogAnimationName);
                    PrefabProps.AnimationSystem.StopHeadAnimation(HeadMorph.HeadMorphType.Viseme);
                    PrefabProps.NpcSubtitles.HideSubtitles();
                }

                return true;
            }

            return false;
        }
    }
}
