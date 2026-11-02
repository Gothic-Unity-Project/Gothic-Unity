using System;
using System.Linq;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Services.Config;
using Reflex.Attributes;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Adapters.Animations.Morph
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


        /// <summary>
        /// DeveloperConfig.EnableFaceAnimations: mood, talking mouth and blinking are blended like the engine does.
        /// </summary>
        protected override bool IsBlendingMorphs => _configService.Dev.EnableFaceAnimations;

        // DeveloperConfig.EnableFaceAnimations: a face animation from an MDS event (T_HURT loops) is held this long.
        public const float EventFaceAnimationSeconds = 1f;

        protected override void Start()
        {
            base.Start();

            // DeveloperConfig.EnableFaceAnimations: the engine blinks on its own (the MMS's R_EYESBLINK) - the blended
            // blink no longer wipes out the rest of the face. Mdl_ApplyRandomFaceAni may have set it already.
            if (!_configService.Dev.EnableNpcEyeBlinking && !_configService.Dev.EnableFaceAnimations)
                return;
            var blinkName = GetAnimationNameByType(HeadMorphType.Eyesblink);
            if (RandomAnimations.Any(i => i.animationName.EqualsIgnoreCase(blinkName)))
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

        /// <summary>
        /// DeveloperConfig.EnableFaceAnimations: Mdl_StartFaceAni / an MDS morph event / talking. intensity = weight
        /// (0..1), holdSeconds: &lt; 0 until replaced or stopped, 0 = the animation's own duration.
        /// </summary>
        public void StartFaceAnimation(string animationName, float intensity, float holdSeconds)
        {
            StartAnimation(HeadName, animationName, intensity, holdSeconds);
        }

        public void StopFaceAnimation(string animationName)
        {
            if (IsAnimationRunning(animationName))
                StopAnimation(animationName);
        }

        /// <summary>
        /// DeveloperConfig.EnableFaceAnimations: Mdl_ApplyRandomFaceAni - a face animation played again and again
        /// (R_EYESBLINK): after timeMin±timeMinVar with probability probMin, otherwise after timeMax±timeMaxVar.
        /// </summary>
        public void ApplyRandomFaceAnimation(string animationName, float timeMin, float timeMinVar, float timeMax,
            float timeMaxVar, float probMin)
        {
            var index = RandomAnimations.FindIndex(i => i.animationName.EqualsIgnoreCase(animationName));
            var entry = (morphMeshName: HeadName, animationName, firstTimeAverage: timeMin,
                firstTimeVariable: timeMinVar, secondTimeAverage: timeMax, secondTimeVariable: timeMaxVar,
                probabilityOfFirst: probMin, timer: 0f);
            if (index >= 0)
            {
                RandomAnimations[index] = entry;
                return;
            }
            RandomAnimations.Add(entry);
            RandomAnimationTimers.Add(timeMax);
        }

        public string GetAnimationNameByType(HeadMorphType type)
        {
            return type switch
            {
                HeadMorphType.Neutral => "S_NEUTRAL",
                HeadMorphType.Friendly => "S_FRIENDLY",
                HeadMorphType.Angry => "S_ANGRY",
                HeadMorphType.Hostile => "S_HOSTILE",
                HeadMorphType.Frightened => "S_FRIGHTENED",
                HeadMorphType.Eyesclosed => "S_EYESCLOSED",
                HeadMorphType.Viseme => "VISEME",
                HeadMorphType.Eat => "T_EAT",
                HeadMorphType.Hurt => "T_HURT",
                HeadMorphType.Eyesblink => "R_EYESBLINK",
                _ => throw new Exception($"AnimationType >{type}< not yet handled for head morphing.")
            };
        }

        public HeadMorphType GetAnimationTypeByName(string name)
        {
            if (name.ContainsIgnoreCase("EAT"))
                return HeadMorphType.Eat;
            if (name.ContainsIgnoreCase("HURT"))
                return HeadMorphType.Hurt;
            if (name.ContainsIgnoreCase("VISEME"))
                return HeadMorphType.Viseme;

            Logger.LogError($"{name} as morphMeshType not yet mapped.", LogCat.Animation);
            
            // If nothing found, we return the hurt face. Meme potential? ;-)
            return HeadMorphType.Hurt;
        }
    }
}
