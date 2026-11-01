using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Player;
using Gothic.Core.Services.Vobs;
using Gothic.Tests.Behaviour.Driver;
using Gothic.Tests.Behaviour.Runner;
using Gothic.Tests.Behaviour.Session;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Models;
using NUnit.Framework;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Tests.Behaviour.Steps.Vobs
{
    /// <summary>
    /// Items lying in the world (put there like Wld_InsertItem does) and the hero taking them or taking things out of
    /// his backpack. Items are named by their Daedalus instance (G1's apple: ITFOAPPLE).
    /// </summary>
    public class ItemSteps
    {
        private const int _settleFrames = 2;
        // The simulator's hand is steered this close to the item's middle, a little above it.
        private const float _gripToleranceMeters = 0.03f;
        private const float _gripAboveMeters = 0.03f;

        [Inject] private readonly VobService _vobService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly PlayerService _playerService;

        private readonly ScenarioScope _scope;
        // The items this scenario put into the world, by instance.
        private readonly Dictionary<string, VobContainer> _lying = new();


        public ItemSteps(ScenarioScope scope)
        {
            _scope = scope;
        }

        private HandDriver Hands => _scope.Driver<HandDriver>();

        /// <summary>
        /// A world item from now on (VobService.InsertItemAt: saved with the world, NPCs see it as lying around),
        /// built before the step ends. Gone again when the scenario ends.
        /// </summary>
        [Step("(?:a|an) \"(.*)\" lies (\\d+(?:\\.\\d+)?) m in front of the hero")]
        public IEnumerator ItemLiesInFrontOfTheHero(string instance, float meters)
        {
            var place = _scope.Driver<PlaceDriver>();
            var item = _vobService.InsertItemAt(instance, place.GroundInFrontOfHero(meters),
                Quaternion.LookRotation(place.HeroForward));
            _lying[instance] = item;
            _scope.Undo($"remove the {instance}", () => _vobService.RemoveWorldItem(item));

            // Lazy loaded like every VOB: built once the culling finds it near the camera.
            yield return Wait.Until(() => IsBuilt(item), Wait.DefaultSeconds, $"the {instance} built",
                () => "it is still waiting to be built");
        }

        /// <summary>
        /// Npc_OwnedByNpc compares the item's owner with the NPC. The game keeps one item data per instance
        /// (VmCacheService), so every item of the instance belongs to the NPC until the scenario ends.
        /// </summary>
        [Step("the \"(.*)\" in front of the hero belongs to \"(.*)\"")]
        public void TheItemBelongsTo(string instance, string owner)
        {
            var npc = _scope.Driver<NpcDriver>().Find(owner) ?? throw new AssertionException($"{owner} isn't in " +
                "the world");
            var item = _vmCacheService.TryGetItemData(instance) ??
                       throw new AssertionException($"The scripts have no item \"{instance}\"");

            var previous = item.Owner;
            item.Owner = npc.Instance.Index;
            _scope.Undo($"{instance} owner back to {previous}", () => item.Owner = previous);
        }

        /// <summary>
        /// The right hand grabs the item the scenario put in front of the hero, through the VR layer's grab events.
        /// </summary>
        [Step("the hero takes the \"(.*)\" lying in front of him")]
        public IEnumerator TheHeroTakesTheItemLyingInFrontOfHim(string instance)
        {
            if (!_lying.TryGetValue(instance, out var item))
                throw new AssertionException($"The scenario put no {instance} in front of the hero");

            Hands.Grab(VrHandSide.Right, item);
            _scope.Undo("release the right hand", () => Hands.Release(VrHandSide.Right));
            yield return Wait.Frames(_settleFrames);
        }

        /// <summary>
        /// The real grab (@input): the simulator's hand reaches for the item the scenario put in front of the hero and
        /// grips it; XR Interaction Toolkit and the VR layer do the rest.
        /// </summary>
        [Step("the hero grips the \"(.*)\" lying in front of him with his (left|right) hand")]
        public IEnumerator TheHeroGripsTheItemLyingInFrontOfHim(string instance, VrHandSide side)
        {
            if (!_lying.TryGetValue(instance, out var item))
                throw new AssertionException($"The scenario put no {instance} in front of the hero");

            var input = _scope.Driver<InputDriver>();
            _scope.Undo("let go of every key", input.ReleaseAll);
            _scope.UndoOverFrames($"let go of the {instance}", () => input.LetGo(side));

            var middle = GetMiddle(item);
            Logger.Log($"[BehaviourTests] The {side} hand reaches for the {instance} at {middle} (placed at " +
                $"{item.Go.transform.position}, layer {LayerMask.LayerToName(item.Go.layer)}), the hero stands at " +
                $"{_scope.Driver<PlaceDriver>().HeroPosition}", LogCat.Debug);
            yield return input.MoveHandTo(side, middle + Vector3.up * _gripAboveMeters, _gripToleranceMeters);
            yield return input.Grip(side);
        }

        /// <summary>
        /// Like taking it out of the backpack: in the inventory first, then a new item (no world item) in the hand.
        /// </summary>
        [Step("the hero takes (?:a|an) \"(.*)\" out of his backpack")]
        public IEnumerator TheHeroTakesAnItemOutOfHisBackpack(string instance)
        {
            _playerService.AddItem(instance);
            _scope.Undo($"take the {instance} out of the inventory", () => _playerService.RemoveItem(instance));

            var item = Hands.CreateItem(instance, VrHandSide.Right);
            _scope.Undo($"remove the {instance}", () => _vobService.RemoveWorldItem(item));
            Hands.Grab(VrHandSide.Right, item);
            _scope.Undo("release the right hand", () => Hands.Release(VrHandSide.Right));
            yield return Wait.Frames(_settleFrames);
        }

        /// <summary>
        /// The middle of what can be touched of the item (its active colliders), else of what is drawn. Parts without
        /// a size (an empty renderer's bounds sit at the world's origin) don't count.
        /// </summary>
        private static Vector3 GetMiddle(VobContainer item)
        {
            var touched = item.Go.GetComponentsInChildren<Collider>()
                .Where(collider => collider.enabled && !collider.isTrigger)
                .Select(collider => collider.bounds);
            var drawn = item.Go.GetComponentsInChildren<Renderer>()
                .Where(renderer => renderer.enabled)
                .Select(renderer => renderer.bounds);
            var bounds = touched.Where(HasSize).ToList();
            if (bounds.Count == 0)
                bounds = drawn.Where(HasSize).ToList();
            if (bounds.Count == 0)
                return item.Go.transform.position;

            var all = bounds[0];
            foreach (var part in bounds)
                all.Encapsulate(part);
            return all.center;
        }

        private static bool HasSize(Bounds bounds)
        {
            return bounds.size.sqrMagnitude > 0.000001f;
        }

        private static bool IsBuilt(VobContainer item)
        {
            return item.Go != null && item.Go.TryGetComponent(out VobLoader loader) && loader.IsLoaded &&
                   VrInteractable.Find(item.Go.transform) != null;
        }
    }
}
