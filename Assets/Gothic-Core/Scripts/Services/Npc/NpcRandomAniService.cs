using System.Collections;
using System.Collections.Generic;
using Gothic.Core.Extensions;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;

namespace Gothic.Core.Services.Npc
{
    /// <summary>
    /// Random animations within a state animation, like the engine's zCModel::SetRandAni / SetRandAniFreq
    /// (Mdl_ApplyRandomAni, Mdl_ApplyRandomAniFreq): while an NPC's state animation loops - a monster eating (S_EAT), a
    /// knocked out human (S_WOUNDED), a bathing babe - one of its random animations plays now and then, on average every
    /// "frequency" seconds, and leads back into the state animation. The reference engine (OpenGothic) leaves them out.
    /// </summary>
    public class NpcRandomAniService
    {
        // Without Mdl_ApplyRandomAniFreq (the scripts always set it, 3-8 s).
        private const float _defaultFrequency = 5f;
        private const float _tickSeconds = 0.25f;

        [Inject] private readonly UnityMonoService _unityMonoService;

        private readonly HashSet<NpcContainer> _npcs = new();
        private readonly List<NpcContainer> _gone = new();
        private Coroutine _ticking;

        public void AddRandomAni(NpcInstance npc, string stateAnimation, string randomAnimation)
        {
            var container = npc?.GetUserData();
            if (container?.Props == null || string.IsNullOrEmpty(stateAnimation) ||
                string.IsNullOrEmpty(randomAnimation))
                return;

            if (!container.Props.RandomAnis.TryGetValue(stateAnimation, out var randomAnimations))
                container.Props.RandomAnis[stateAnimation] = randomAnimations = new List<string>();
            if (!randomAnimations.Contains(randomAnimation))
                randomAnimations.Add(randomAnimation);

            _npcs.Add(container);
            _ticking ??= _unityMonoService.StartCoroutine(Tick());
        }

        public void SetRandomAniFrequency(NpcInstance npc, string stateAnimation, float seconds)
        {
            var container = npc?.GetUserData();
            if (container?.Props == null || string.IsNullOrEmpty(stateAnimation))
                return;

            container.Props.RandomAniFrequencies[stateAnimation] = Mathf.Max(seconds, 0.5f);
        }

        private IEnumerator Tick()
        {
            var wait = new WaitForSeconds(_tickSeconds);
            while (true)
            {
                yield return wait;

                foreach (var npc in _npcs)
                {
                    if (npc.Props == null)
                        _gone.Add(npc);
                    else if (npc.Go != null && npc.Go.activeInHierarchy)
                        Update(npc);
                }

                foreach (var npc in _gone)
                    _npcs.Remove(npc);
                _gone.Clear();
            }
        }

        private void Update(NpcContainer npc)
        {
            var animationSystem = npc.PrefabProps?.AnimationSystem;
            if (animationSystem == null)
                return;

            foreach (var pair in npc.Props.RandomAnis)
            {
                if (!animationSystem.IsPlaying(pair.Key))
                    continue;

                // One random animation at a time - it leads back into the state animation.
                foreach (var randomAnimation in pair.Value)
                {
                    if (animationSystem.IsPlaying(randomAnimation))
                        return;
                }

                var frequency = npc.Props.RandomAniFrequencies.TryGetValue(pair.Key, out var seconds)
                    ? seconds
                    : _defaultFrequency;
                if (npc.Props.NextRandomAniTime <= 0f)
                {
                    npc.Props.NextRandomAniTime = Time.time + frequency * Random.Range(0.5f, 1.5f);
                    return;
                }

                if (Time.time < npc.Props.NextRandomAniTime)
                    return;

                animationSystem.PlayAnimation(pair.Value[Random.Range(0, pair.Value.Count)]);
                npc.Props.NextRandomAniTime = Time.time + frequency * Random.Range(0.5f, 1.5f);
                return;
            }
        }
    }
}
