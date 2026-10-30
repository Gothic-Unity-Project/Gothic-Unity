using System;
using Gothic.Core.Extensions;

namespace Gothic.Core.Adapters.Animations.Morph
{
    /// <summary>
    /// The NPC's face like the engine: the mouth moves while talking (VISEME), Mdl_StartFaceAni moods (S_FRIENDLY,
    /// S_ANGRY, S_FRIGHTENED...), face animations of MDS events (T_HURT, T_EAT) and blinking on its own, blended on top
    /// of each other with the MMB's blend in/out. Found on BoroBongo's branch.
    /// </summary>
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

        // A face animation from an MDS event (T_HURT loops) is held this long.
        private const float _eventFaceAnimationSeconds = 1f;

        public string HeadName;

        protected override bool IsBlendingMorphs => true;

        // Faces are seen up to a few meters only.
        protected override float MaxBlendDistance => 12f;


        protected override void Start()
        {
            base.Start();

            // Mdl_ApplyRandomFaceAni may have set the blink already.
            var blinkName = GetAnimationNameByType(HeadMorphType.Eyesblink);
            if (RandomAnimations.Exists(i => i.animationName.EqualsIgnoreCase(blinkName)))
                return;

            RandomAnimations.Add(new()
            {
                morphMeshName = HeadName,
                animationName = blinkName,
                firstTimeAverage = 0.15f,
                firstTimeVariable = 0.1f,
                secondTimeAverage = 3.8f,
                secondTimeVariable = 1.0f,
                probabilityOfFirst = 0.2f
            });
            RandomAnimationTimers.Add(3.8f * 2); // secondTimeAverage * 2 seconds);
        }

        /// <summary>
        /// Mdl_StartFaceAni, an MDS morph event or talking. intensity = weight (0..1), holdSeconds: &lt; 0 until
        /// replaced or stopped, 0 = the animation's own duration.
        /// </summary>
        public void StartFaceAnimation(string animationName, float intensity, float holdSeconds)
        {
            StartAnimation(HeadName, animationName, intensity, holdSeconds);
        }

        /// <summary>
        /// A face animation of an MDS event (T_HURT, T_EAT), held a moment.
        /// </summary>
        public void StartEventFaceAnimation(string animationName)
        {
            StartAnimation(HeadName, animationName, 1f, _eventFaceAnimationSeconds);
        }

        /// <summary>
        /// Mdl_ApplyRandomFaceAni: a face animation played again and again (R_EYESBLINK): after timeMin±timeMinVar with
        /// probability probMin, otherwise after timeMax±timeMaxVar.
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
    }
}
