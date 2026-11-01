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
        /// Where the hero stands (his feet).
        /// </summary>
        public Vector3 HeroPosition => _npcService.GetHeroGameObject().transform.position;

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
    }
}
