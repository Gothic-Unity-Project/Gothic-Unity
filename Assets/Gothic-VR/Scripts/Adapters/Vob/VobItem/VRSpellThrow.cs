using System.Collections.Generic;
using System.Linq;
using Gothic.Core;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Meshes;
using Gothic.Core.Services.Npc;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Services;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Vob.VobItem
{
    /// <summary>
    /// Throwable spells (Immersion option "throwableSpells"): the hand's swing gives direction, speed, gravity and
    /// homing, the spell flies as a Projectile with the effect from the hand and its collision effects (VRSpellFx).
    /// Worker of VRRuneCaster (new VRSpellThrow(...).Inject()) - the caster charges, this class throws.
    /// </summary>
    public class VRSpellThrow
    {
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly MeshService _meshService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;

        // Throw speed: the spell's own speed (emtrjeasevel) scaled by the swing - a lazy flick is slow.
        private const float _maxSwingFactor = 1.5f;
        private const float _handVelocityWindow = 0.1f;
        private const float _minHomingSwingFactor = 0.5f;
        private const float _minThrowDirectionSpeed = 0.2f;
        private const float _droppedSpellSpeed = 0.1f;
        private const float _homingConeDegrees = 12f;
        private const float _homingDegreesPerSecond = 90f;
        private const float _defaultThrowRange = 30f;

        private readonly ParticleEffectEmitKeyInstance _castKey;
        private readonly string _mfxName;
        private readonly int _spellId;
        private readonly VRSpellFx _fx;
        private readonly Queue<(float time, Vector3 position)> _handSamples = new();
        // Cached on first use: the rig's head (simulator throw direction).
        private Transform _head;

        // What the swing of the last throw gives the projectile.
        private float _speedFactor = 1f;
        private float _gravityScale;
        private bool _isHoming = true;

        /// <summary>
        /// castKey: GetCastKey's result for the spell (not null).
        /// </summary>
        public VRSpellThrow(ParticleEffectEmitKeyInstance castKey, string mfxName, int spellId, VRSpellFx fx)
        {
            _castKey = castKey;
            _mfxName = mfxName;
            _spellId = spellId;
            _fx = fx;
        }

        /// <summary>
        /// Projectile spells are thrown: the VISUALFX CAST key flies to a target (emtrjmode_s TARGET) with a speed and
        /// collision - Firebolt, Fireball, Thunderbolt, Icecube, ... (G2/mod spells automatically). Null = cast as
        /// before.
        /// </summary>
        public static ParticleEffectEmitKeyInstance GetCastKey(VmCacheService vmCacheService, string mfxName)
        {
            if (mfxName == null)
                return null;

            var castKey = vmCacheService.TryGetVfxEmitKey($"spellFX_{mfxName}_KEY_CAST");
            var isThrowable = castKey != null &&
                              castKey.EmTrjModeS.Contains("TARGET", System.StringComparison.OrdinalIgnoreCase) &&
                              castKey.EmTrjEaseVel > 0f && castKey.EmCheckCollision != 0;

            Logger.Log($"[VRRuneCaster] spellFX_{mfxName}: throwable={isThrowable} " +
                       $"(trj={castKey?.EmTrjModeS ?? "-"}, vel={castKey?.EmTrjEaseVel ?? 0}, " +
                       $"coll={castKey?.EmCheckCollision ?? 0}, vis={castKey?.VisNameS ?? "-"})", LogCat.VR);
            return isThrowable ? castKey : null;
        }

        public void TrackHand(VrHandSide handSide)
        {
            var handGo = _vrPlayerService.GetHandModelGo(handSide);
            if (handGo == null)
                return;

            _handSamples.Enqueue((Time.time, handGo.transform.position));
            while (_handSamples.Count > 2 && Time.time - _handSamples.Peek().time > _handVelocityWindow)
                _handSamples.Dequeue();
        }

        /// <summary>
        /// Released the trigger: the direction of the throw. Thrown like a ball out of the hand - the swing gives
        /// direction, speed and how straight it flies. A full swing flies straight like in Gothic, half a swing arcs,
        /// no swing drops it from the hand. Only a real throw homes on NPCs and hit targets (and then ignores the
        /// gravity). Before it flew where the camera looked, slowly but straight, even without moving the hand.
        /// The simulator can't swing: where you look, at full speed.
        /// </summary>
        public Vector3 Aim(GameObject handGo, bool isWasd)
        {
            Vector3 direction;
            if (!isWasd)
            {
                var handVelocity = GetHandVelocity();
                var swingFactor = Mathf.Clamp(handVelocity.magnitude / VRConstants.Magic.SpellThrowFullSwingSpeed, 0f,
                    _maxSwingFactor);
                direction = handVelocity.magnitude >= _minThrowDirectionSpeed ? handVelocity.normalized : Vector3.down;
                _speedFactor = swingFactor;
                _gravityScale = Mathf.Clamp01(1f - swingFactor);
                _isHoming = swingFactor >= _minHomingSwingFactor;
                Logger.Log($"[VRSpellThrow] Throw: hand {handVelocity.magnitude:F1} m/s, swing {swingFactor:F2}, " +
                           $"gravity {_gravityScale:F2}, homing {_isHoming}", LogCat.VR);
            }
            else
            {
                if (_head == null)
                    _head = _vrPlayerService.Rig?.Head;
                direction = _head != null ? _head.forward : handGo.transform.forward;
                _speedFactor = 1f;
                _gravityScale = 0f;
                _isHoming = true;
            }
            return direction.sqrMagnitude < 0.01f ? handGo.transform.forward : direction;
        }

        /// <summary>
        /// The spell flies from origin: level - its damage level, investLevel - its collision effect's level, damage -
        /// the hit's damage. The effect in the hand (VRSpellFx) flies with it.
        /// </summary>
        public void Throw(Vector3 origin, Vector3 direction, int level, int investLevel, int damage)
        {
            var hero = _npcService.GetHeroContainer();
            var mfxName = _mfxName;
            var speed = _castKey.EmTrjEaseVel / 100f * _speedFactor * VRConstants.Magic.SpellThrowSpeedMultiplier;
            var range = GetThrowRange(mfxName);

            var projectileGo = new GameObject($"SpellProjectile ({mfxName})");
            projectileGo.transform.SetPositionAndRotation(origin, Quaternion.LookRotation(direction));

            // The charge the player saw in the hand is what flies (with its invest level size).
            var pfx = _fx.TakeHandVfx();
            if (pfx != null)
            {
                pfx.transform.SetParent(projectileGo.transform, false);
                pfx.transform.localPosition = Vector3.zero;
            }
            else
            {
                var castPfxName = string.IsNullOrEmpty(_castKey.VisNameS) ? $"MFX_{mfxName}_INIT" : _castKey.VisNameS;
                pfx = _meshService.CreateVobPfx(castPfxName, parent: projectileGo);
            }
            if (pfx != null)
            {
                foreach (var particles in projectileGo.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var main = particles.main;
                    main.loop = true;
                    particles.Play();
                }
            }

            var light = projectileGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 3f;
            light.intensity = 2f;
            light.color = GetSpellLightColor(mfxName);

            var homingTarget = _isHoming ? FindHomingTarget(hero, origin, direction, range) : null;
            var projectile = projectileGo.AddComponent<Projectile>();
            projectile.Owner = hero;
            projectile.Velocity = direction * Mathf.Max(speed, _droppedSpellSpeed);
            projectile.UseGravity = _gravityScale > 0f;
            projectile.GravityScale = _gravityScale;
            projectile.AllowAutoAim = _isHoming;
            projectile.Radius = 0.15f;
            projectile.MaxDistance = range;
            projectile.HomingTarget = homingTarget;
            projectile.HomingDegreesPerSecond = _homingDegreesPerSecond;

            // The rune can be gone before the spell lands - the lambdas keep this worker and its VRSpellFx.
            var fx = _fx;
            var baseFx = _vmCacheService.TryGetVfxData($"spellFX_{mfxName}");
            var spellId = _spellId;

            projectile.OnNpcHit = (npc, position, normal) =>
            {
                Logger.Log($"[VRSpellThrow] {mfxName} hit {npc.Instance.GetName(NpcNameSlot.Slot0)} " +
                           $"(level {level}, dmg {damage})", LogCat.VR);
                // FightService reads the spell from the caster's ActiveSpell (fire, AOE, damage type): it was the
                // Light spell (0) once the rune was let go before the fireball landed - no fire.
                var activeSpell = hero.ActiveSpell;
                hero.ActiveSpell = spellId;
                GlobalEventDispatcher.SpellHit.Invoke(hero, npc, position, damage);
                hero.ActiveSpell = activeSpell;
                fx.SpawnCollideFx(baseFx?.EmFxCollDynS, investLevel, position, normal);
                Object.Destroy(projectileGo);
            };
            projectile.OnWorldHit = (position, normal) =>
            {
                fx.SpawnCollideFx(baseFx?.EmFxCollStatS, investLevel, position, normal);
                // A fire storm on the ground: its burning area still damages the NPCs around (FightService).
                if (mfxName != null && SpellConst.SpreadEffectNames.Contains(mfxName))
                {
                    var activeSpell = hero.ActiveSpell;
                    hero.ActiveSpell = spellId;
                    GlobalEventDispatcher.SpellSpreadHit.Invoke(hero, position, damage);
                    hero.ActiveSpell = activeSpell;
                }
                Object.Destroy(projectileGo);
            };
            projectile.OnExpired = () => Object.Destroy(projectileGo);

            Logger.Log($"[VRSpellThrow] Threw {mfxName} damage level {level} invest level {investLevel} " +
                       $"at {speed:F1} m/s, " +
                       $"homing={homingTarget?.Instance?.GetName(NpcNameSlot.Slot0) ?? "none"}", LogCat.VR);
        }

        private Vector3 GetHandVelocity()
        {
            if (_handSamples.Count < 2)
                return Vector3.zero;

            var oldest = _handSamples.Peek();
            var newest = _handSamples.Last();
            var deltaTime = newest.time - oldest.time;
            return deltaTime > 0f ? (newest.position - oldest.position) / deltaTime : Vector3.zero;
        }

        /// <summary>
        /// Gothic's TARGET trajectory homes on the focus NPC. VR: the living NPC closest to the throw direction
        /// within a small cone - helps without taking the aiming away.
        /// </summary>
        private NpcContainer FindHomingTarget(NpcContainer hero, Vector3 origin, Vector3 direction, float range)
        {
            NpcContainer best = null;
            var bestAngle = _homingConeDegrees;
            foreach (var npc in _multiTypeCacheService.NpcCache)
            {
                if (npc == hero || npc?.Go == null || !npc.Go.activeInHierarchy)
                    continue;
                if (npc.Props.BodyState is VmGothicEnums.BodyState.BsDead or VmGothicEnums.BodyState.BsUnconscious)
                    continue;

                var toNpc = npc.Go.transform.position + Vector3.up * 0.4f - origin;
                if (toNpc.magnitude > range)
                    continue;

                var angle = Vector3.Angle(direction, toNpc);
                if (angle >= bestAngle)
                    continue;

                bestAngle = angle;
                best = npc;
            }
            return best;
        }

        private float GetThrowRange(string mfxName)
        {
            var targetRange = _vmCacheService.TryGetVfxData($"spellFX_{mfxName}")?.EmTrjTargetRange ?? 0f;
            // emTrjTargetRange is in meters in VISUALFX (Fireball: 20).
            return targetRange > 0f ? Mathf.Max(targetRange, _defaultThrowRange) : _defaultThrowRange;
        }

        private static Color GetSpellLightColor(string mfxName)
        {
            if (mfxName == null)
                return new Color(1f, 0.6f, 0.3f);
            if (mfxName.Contains("Ice", System.StringComparison.OrdinalIgnoreCase))
                return new Color(0.6f, 0.8f, 1f);
            if (mfxName.Contains("Thunder", System.StringComparison.OrdinalIgnoreCase) ||
                mfxName.Contains("Zap", System.StringComparison.OrdinalIgnoreCase) ||
                mfxName.Contains("Lightning", System.StringComparison.OrdinalIgnoreCase))
                return new Color(0.7f, 0.7f, 1f);
            if (mfxName.Contains("Death", System.StringComparison.OrdinalIgnoreCase))
                return new Color(0.5f, 1f, 0.5f);
            return new Color(1f, 0.6f, 0.3f);
        }
    }
}
