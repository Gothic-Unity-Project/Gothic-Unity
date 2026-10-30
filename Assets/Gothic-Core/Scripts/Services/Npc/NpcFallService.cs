using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Npc;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Npc
{
    /// <summary>
    /// NPCs falling, like the engine (OpenGothic MoveAlgo and DamageCalculator::damageFall): a walking NPC drops by
    /// physics, and landing from higher than its guild's FALLDOWN_HEIGHT costs FALLDOWN_DAMAGE per meter beyond it, less
    /// its fall protection - a negative protection makes it immune. A deadly fall kills. Before, NPCs that ran or fell
    /// off a cliff landed unhurt. Found on BoroBongo's branch.
    /// </summary>
    public class NpcFallService
    {
        // Vertical speed (m/s) from which the NPC is in the air, and up to which it has landed again.
        private const float _fallingSpeed = -3f;
        private const float _landedSpeed = -0.5f;

        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly NpcService _npcService;

        /// <summary>
        /// Called by every awake NPC once per frame.
        /// </summary>
        public void Tick(NpcContainer npc)
        {
            var props = npc.Props;
            var body = npc.PrefabProps?.ColliderRootMotion;
            if (body == null || !body.TryGetComponent<Rigidbody>(out var rigidbody) || rigidbody.isKinematic)
            {
                // Standing, sitting, playing an animation: no physics, no fall.
                props.FallStartY = null;
                return;
            }

            var y = rigidbody.position.y;
            var verticalSpeed = rigidbody.velocity.y;
            if (props.FallStartY == null)
            {
                if (verticalSpeed < _fallingSpeed)
                    props.FallStartY = props.LastGroundY;
                else
                    props.LastGroundY = y;
                return;
            }

            if (verticalSpeed < _landedSpeed)
                return;

            var height = props.FallStartY.Value - y;
            props.FallStartY = null;
            props.LastGroundY = y;
            Land(npc, height);
        }

        /// <summary>
        /// The NPC landed after falling this far (m).
        /// </summary>
        public void Land(NpcContainer npc, float height)
        {
            var damage = GetFallDamage(npc, height);
            if (damage <= 0)
                return;

            var hitPoints = npc.Vob.GetAttribute((int)NpcAttribute.HitPoints);
            Logger.Log($"[NpcFall] {npc.Instance.GetName(NpcNameSlot.Slot0)} fell {height:F1} m: {damage} damage",
                LogCat.Npc);
            _npcService.SetAttribute(npc, NpcAttribute.HitPoints, Mathf.Max(0, hitPoints - damage));
        }

        /// <summary>
        /// OpenGothic DamageCalculator::damageFall: FALLDOWN_DAMAGE per meter (cm values) beyond FALLDOWN_HEIGHT, less the
        /// fall protection. 0 below that height or with a negative (immune) protection.
        /// </summary>
        public int GetFallDamage(NpcContainer npc, float height)
        {
            var guildValues = _gameStateService.GuildValues;
            if (guildValues == null)
                return 0;

            var guild = npc.Instance.GetValuesGuild();
            var fallDownHeight = guildValues.GetFallDownHeight(guild);
            var damagePerMeter = guildValues.GetFallDownDamage(guild);
            var protection = npc.Vob.GetProtection((int)DamageType.Fall);
            if (protection < 0)
                return 0;

            return Mathf.Max(0, (int)(damagePerMeter * (height * 100f - fallDownHeight) / 100f) - protection);
        }
    }
}
