using System.Collections.Generic;
using GUZ.Core.Extensions;
using UnityEngine;
using ZenKit;

namespace GUZ.Core.Models.Animations
{
    public class AnimationTrack
    {
        public enum Type
        {
            Animation,
            Alias
        }

        public Type TrackType = Type.Animation;
        private IAnimation _animation;
        private IAnimationAlias _animationAlias;

        // AnimationType (Animation/Alias) specific values
        public int Layer => TrackType == Type.Animation ? _animation.Layer : _animationAlias.Layer;
        public string NextAni => TrackType == Type.Animation ? _animation.Next : _animationAlias.Next;
        public float BlendIn => TrackType == Type.Animation ? _animation.BlendIn : _animationAlias.BlendIn;
        public float BlendOut => TrackType == Type.Animation ? _animation.BlendOut : _animationAlias.BlendOut;
        public AnimationFlags Flags => TrackType == Type.Animation ? _animation.Flags : _animationAlias.Flags;
        public string AliasName => TrackType == Type.Animation ? null : _animationAlias.Name;
        public AnimationDirection AniDir => TrackType == Type.Animation ? _animation.Direction : _animationAlias.Direction;

        // To ensure, Animation/Alias specific values are always used, we make actual IAnimation private. Therefore we need to expose
        // remaining properties.
        public string Name => _animation.Name;
        public int FirstFrame => _animation.FirstFrame;
        public int EventTagCount => _animation.EventTagCount;
        public int ParticleEffectCount => _animation.ParticleEffectCount;
        public int ParticleEffectStopCount => _animation.ParticleEffectStopCount;
        public int SoundEffectCount => _animation.SoundEffectCount;
        public int SoundEffectGroundCount => _animation.SoundEffectGroundCount;
        public int MorphAnimationCount => _animation.MorphAnimationCount;
        public int CameraTremorCount => _animation.CameraTremorCount;
        public List<IEventTag> EventTags => _animation.EventTags;
        public List<IEventParticleEffect> ParticleEffects => _animation.ParticleEffects;
        public List<IEventParticleEffectStop> ParticleEffectsStop => _animation.ParticleEffectsStop;
        public List<IEventSoundEffect> SoundEffects => _animation.SoundEffects;
        public List<IEventSoundEffectGround> SoundEffectsGround => _animation.SoundEffectsGround;
        public List<IEventMorphAnimation> MorphAnimations => _animation.MorphAnimations;
        public List<IEventCameraTremor> CameraTremors => _animation.CameraTremors;

        public string[] BoneNames;
        public Dictionary<string, int> BoneNamesDictionary; // this exists as it's faster to search in a dict instead of linear array search
        public int BoneCount;
        public int FrameCount;
        public AnimationKeyFrame[] KeyFrames;
        public IModelAnimation ModelAnimation;
        public float Duration;
        public float FrameTime;

        public bool IsMoving;
        public Vector3 MovementSpeed;


        public AnimationTrack(IAnimation anim, IAnimationAlias animAlias, IModelAnimation modelAnimation)
        {
            TrackType = animAlias == null ? Type.Animation : Type.Alias;
            _animation = anim;
            _animationAlias = animAlias;
            ModelAnimation = modelAnimation;
        }

        public void GetBonePose(int boneIndex, int frameIndex, out Vector3 position, out Quaternion rotation)
        {
            var keyFrame = KeyFrames[frameIndex * BoneCount + boneIndex];

            position = keyFrame.Position;
            rotation = keyFrame.Rotation;
        }

        public bool IsSameAnimation(AnimationTrack otherTrack)
        {
            var nameOfSelf = TrackType == Type.Animation ? _animation.Name : _animationAlias.Name;
            var nameOfOther = otherTrack.TrackType == Type.Animation ? otherTrack.Name : otherTrack.AliasName;
            
            return nameOfSelf.EqualsIgnoreCase(nameOfOther);
        }
    }
}
