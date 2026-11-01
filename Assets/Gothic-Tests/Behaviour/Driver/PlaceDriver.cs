using System.Collections;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Npc;
using Gothic.Core.Creator;
using Gothic.Core.Services.World;
using Gothic.Tests.Behaviour.Session;
using NUnit.Framework;
using Reflex.Attributes;
using UnityEngine;

namespace Gothic.Tests.Behaviour.Driver
{
    /// <summary>
    /// Puts the hero somewhere and waits until the place around him is built, like a teleport in the game: the view is
    /// black meanwhile (SurroundingsService), so the scenario starts with the VOBs and NPCs around him in place.
    /// Also where "in front of the hero" is.
    /// </summary>
    public class PlaceDriver
    {
        [Inject] private readonly WayNetService _wayNetService;
        [Inject] private readonly ContextInteractionService _contextInteractionService;
        [Inject] private readonly SurroundingsService _surroundingsService;
        [Inject] private readonly NpcService _npcService;

        // Free spots for NPCs are looked for in these steps around the hero, up to 90 degrees to either side.
        private const float _freeSpotStepDegrees = 10f;
        private const int _freeSpotSteps = 9;


        /// <summary>
        /// A way point or free point of the loaded world.
        /// </summary>
        public IEnumerator TeleportToWayPoint(string name)
        {
            var point = _wayNetService.GetWayNetPoint(name);
            if (point == null)
                throw new AssertionException($"{GameSession.World} has no way point or free point \"{name}\"");

            _contextInteractionService.SetViewHidden(true);
            try
            {
                _contextInteractionService.TeleportPlayerTo(point.Position, point.Rotation);
                yield return Wait.For(_surroundingsService.BuildAroundAsync(point.Position));
            }
            finally
            {
                _contextInteractionService.SetViewHidden(false);
            }
        }

        /// <summary>
        /// Where the hero stands: the ground point under his head, as the game measures distances to him
        /// (DialogService) - a physical step in the play area moves it, turning doesn't.
        /// </summary>
        public Vector3 HeroPosition
        {
            get
            {
                var hero = _npcService.GetHeroContainer();
                var position = hero.Go.transform.position;
                var head = hero.PrefabProps != null ? hero.PrefabProps.Head : null;
                if (head != null)
                {
                    position.x = head.position.x;
                    position.z = head.position.z;
                }

                return position;
            }
        }

        /// <summary>
        /// His eyes - the hero's head is the camera.
        /// </summary>
        public Vector3 HeadPosition
        {
            get
            {
                var hero = _npcService.GetHeroContainer();
                var head = hero.PrefabProps != null ? hero.PrefabProps.Head : null;
                return head != null ? head.position : hero.Go.transform.position;
            }
        }

        /// <summary>
        /// Where he looks, level with the ground.
        /// </summary>
        public Vector3 HeroForward
        {
            get
            {
                var hero = _npcService.GetHeroContainer();
                var head = hero.PrefabProps != null ? hero.PrefabProps.Head : null;
                var forward = head != null ? head.forward : hero.Go.transform.forward;
                forward.y = 0;
                return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
            }
        }

        /// <summary>
        /// The point this far in front of the hero, at his feet's height.
        /// </summary>
        public Vector3 InFrontOfHero(float meters)
        {
            return HeroPosition + HeroForward * meters;
        }

        /// <summary>
        /// The ground this far in front of the hero (the first surface below the point).
        /// </summary>
        public Vector3 GroundInFrontOfHero(float meters)
        {
            return Probe.GroundBelow(InFrontOfHero(meters));
        }

        /// <summary>
        /// A spot on the ground this far from the hero, as near to straight ahead as possible, where the game would
        /// spawn an NPC without moving it (NpcService.GetFreeAreaAtSpawnPoint).
        /// </summary>
        /// <returns>False if there's none within a quarter circle to either side.</returns>
        public bool TryFindFreeSpotInFrontOfHero(float meters, out Vector3 spot)
        {
            for (var step = 0; step <= _freeSpotSteps; step++)
            {
                foreach (var side in step == 0 ? new[] { 1 } : new[] { 1, -1 })
                {
                    var direction = Quaternion.AngleAxis(side * step * _freeSpotStepDegrees, Vector3.up) * HeroForward;
                    spot = Probe.GroundBelow(HeroPosition + direction * meters);
                    if ((_npcService.GetFreeAreaAtSpawnPoint(spot) - spot).sqrMagnitude < 0.0001f)
                        return true;
                }
            }

            spot = default;
            return false;
        }
    }
}
