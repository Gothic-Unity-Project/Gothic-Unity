using System;
using GUZ.Core.Extensions;
using GUZ.Core.Logging;
using GUZ.Core.Services.Config;
using Reflex.Attributes;
using Logger = GUZ.Core.Logging.Logger;

namespace GUZ.Core.Adapters.Animations.Morph
{
    public class HeadMorph : AbstractMorphAnimation
    {
        public enum HeadMorphType
        {
            Neutral,
            Friendly,
            Angry,
            Hostile,
            Frightened,
            Eyesclosed,
            Eyesblink,
            Eat,
            Hurt,
            Viseme
        }

        [Inject] private readonly ConfigService _configService;


        public string HeadName;


        protected override void Start()
        {
            base.Start();

            if (!_configService.Dev.EnableNpcEyeBlinking)
                return;

            RandomAnimations.Add(new()
            {
                morphMeshName = HeadName,
                animationName = GetAnimationNameByType(HeadMorphType.Eyesblink),
                firstTimeAverage = 0.15f,
                firstTimeVariable = 0.1f,
                secondTimeAverage = 3.8f,
                secondTimeVariable = 1.0f,
                probabilityOfFirst = 0.2f
            });
            RandomAnimationTimers.Add(3.8f * 2); // secondTimeAverage * 2 seconds);
        }

        public void StartAnimation(string headName, HeadMorphType type)
        {
            StartAnimation(headName, GetAnimationNameByType(type));
        }

        /// <summary>
        /// We need to wrap StopAnimation by fetching string name of animation based on HeadMorphType
        /// </summary>
        public void StopAnimation(HeadMorphType type)
        {
            var animationName = GetAnimationNameByType(type);
            StopAnimation(animationName);
        }

        private string GetAnimationNameByType(HeadMorphType type)
        {
            return type switch
            {
                HeadMorphType.Viseme => "VISEME",
                HeadMorphType.Eat => "T_EAT",
                HeadMorphType.Eyesblink => "R_EYESBLINK",
                _ => throw new Exception($"AnimationType >{type}< not yet handled for head morphing.")
            };
        }

        public HeadMorphType GetAnimationTypeByName(string name)
        {
            if (name.ContainsIgnoreCase("EAT"))
            {
                return HeadMorphType.Eat;
            }

            Logger.LogError($"{name} as morphMeshType not yet mapped.", LogCat.Animation);

            // If nothing found, we return the hurt face. Meme potential? ;-)
            return HeadMorphType.Hurt;
        }
    }
}
