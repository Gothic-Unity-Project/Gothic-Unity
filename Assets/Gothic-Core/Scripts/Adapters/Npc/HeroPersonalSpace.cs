using Gothic.Core.Extensions;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Reflex.Attributes;
using UnityEngine;

namespace Gothic.Core.Adapters.Npc
{
    /// <summary>
    /// DeveloperConfig.EnableHeroPersonalSpace: no NPC body inside a small circle around the hero's head - in VR their
    /// models went into the camera (nothing visible) and pushed the hands with the held weapon around.
    /// Only the position is corrected after the NPCs moved this frame: no forces, no AI change. The AI still stops at
    /// its own distance (>= 1.1 m) and hits from ~2 m, so a fight works like before.
    /// </summary>
    public class HeroPersonalSpace : MonoBehaviour
    {
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;
        [Inject] private readonly ConfigService _configService;

        private const float _radius = 1.0f;
        private const float _maxHeightDifference = 2f;

        // DeveloperConfig.EnableBodyPersonalSpace: gap between the camera and the NPC's solid body (not only its root).
        private const float _bodyMargin = 0.3f;
        // NPCs this close get their animated body bounds (hitbox, personal space).
        private const float _bodyBoundsDistance = 12f;

        private void Awake() => this.Inject();

        private static bool IsTalkableHuman(Gothic.Core.Models.Container.NpcContainer npc)
        {
            return npc.Instance.Guild <= (int)VmGothicEnums.Guild.GIL_SEPERATOR_HUM &&
                   (VmGothicEnums.WeaponState)npc.Vob.FightMode == VmGothicEnums.WeaponState.NoWeapon;
        }

        private void LateUpdate()
        {
            if (!_configService.Dev.EnableHeroPersonalSpace)
                return;

            var cam = Camera.main;
            if (cam == null)
                return;

            var heroPosition = cam.transform.position;
            var isBodyBoundsUsed = _configService.Dev.EnableAnimatedNpcHitbox ||
                                   _configService.Dev.EnableBodyPersonalSpace;
            foreach (var npc in _multiTypeCacheService.NpcCache)
            {
                if (npc?.Go == null || !npc.Go.activeInHierarchy || npc.PrefabProps == null || npc.PrefabProps.IsHero())
                    continue;
                var bodyBounds = isBodyBoundsUsed &&
                                 (npc.Go.transform.position - heroPosition).sqrMagnitude <
                                 _bodyBoundsDistance * _bodyBoundsDistance
                    ? NpcBodyBounds.Get(npc)
                    : null;
                // Dead/knocked out bodies lie where they fell (loot them standing on top).
                if (npc.Props.BodyState is VmGothicEnums.BodyState.BsDead or VmGothicEnums.BodyState.BsUnconscious)
                    continue;
                // Humans with their weapon away can be walked up to and grabbed to talk - pushed 1 m away they were out
                // of reach and no NPC could be talked to. Monsters and anyone with a drawn weapon keep their distance.
                if (IsTalkableHuman(npc))
                    continue;

                var npcTransform = npc.Go.transform;
                var offset = npcTransform.position - heroPosition;
                if (Mathf.Abs(offset.y) > _maxHeightDifference)
                    continue;

                offset.y = 0f;
                var distance = offset.magnitude;
                if (distance < _radius)
                {
                    // Exactly on the hero: push it away from where the camera looks.
                    var direction = distance > 0.001f ? offset / distance : -Flatten(cam.transform.forward);
                    npcTransform.position += direction * (_radius - distance);
                }

                PushBodyOut(bodyBounds, npcTransform, heroPosition);
            }
        }

        /// <summary>
        /// DeveloperConfig.EnableBodyPersonalSpace: the root is far enough, but the body may not be - a shadowbeast's
        /// root is at its hind legs, the head 2 m in front went into the camera; an orc leaning into his swing too.
        /// The solid body (arms excluded - weapons and claws still swing at the hero) keeps _bodyMargin.
        /// </summary>
        private void PushBodyOut(NpcBodyBounds bodyBounds, Transform npcTransform, Vector3 heroPosition)
        {
            if (!_configService.Dev.EnableBodyPersonalSpace || bodyBounds == null || !bodyBounds.IsReady)
                return;

            var push = bodyBounds.GetPushOut(heroPosition, _bodyMargin);
            if (push != Vector3.zero)
                npcTransform.position += push;
        }

        private static Vector3 Flatten(Vector3 vector)
        {
            vector.y = 0f;
            return vector.sqrMagnitude > 0.001f ? vector.normalized : Vector3.forward;
        }
    }
}
