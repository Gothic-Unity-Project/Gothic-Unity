using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Meshes;
using Gothic.Core.Services.Vobs;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Models.FavoritesRing;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;
using RingConstants = Gothic.VR.VRConstants.FavoritesRing;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// The favorites ring's slot visuals (VRFavoritesRing adds it): a slot's rune/scroll model, the real weapon/item
    /// shrunk into it, the empty slot's sphere and the front spell's effect preview.
    /// </summary>
    public class VRFavoritesRingSlotViews : MonoBehaviour
    {

        [Inject] private readonly VobService _vobService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly MeshService _meshService;
        [Inject] private readonly DynamicMaterialService _dynamicMaterialService;

        private const string _focusNameCanvas = "NameCanvas";
        private const float _spellModelTilt = -90f;
        private static readonly int _baseColorProperty = Shader.PropertyToID("_BaseColor");
        private static readonly int _colorProperty = Shader.PropertyToID("_Color");
        private static readonly int _cutoffProperty = Shader.PropertyToID("_Cutoff");
        private static readonly Color _emptySlotColor = new(0.6f, 0.6f, 0.6f, 1f);

        private static readonly List<Collider> _colliderBuffer = new();

        private Material _emptySlotMaterial;

        /// <summary>
        /// The VR rig - its body and hand colliders ignore the ring items. Null: no rig.
        /// </summary>
        public IVrRig Rig { get; set; }

        private void Awake()
        {
            this.Inject();
        }

        public FavoritesRingSlotView Create(FavoriteSlot content, Transform parent)
        {
            var item = content.Item;
            var root = new GameObject(item != null ? $"{content.Kind} {content.InstanceName}" : "Favorite (empty)");
            root.transform.SetParent(parent, false);
            var slot = new FavoritesRingSlotView
            {
                Content = content,
                Root = root
            };

            if (item == null)
            {
                slot.Model = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                slot.Model.name = "Empty";
                Destroy(slot.Model.GetComponent<Collider>());
                slot.Model.transform.SetParent(root.transform, false);
                slot.Model.transform.localScale = Vector3.one * (RingConstants.SpellSize * 0.5f);
                _emptySlotMaterial ??= CreateEmptySlotMaterial();
                slot.Model.GetComponent<MeshRenderer>().sharedMaterial = _emptySlotMaterial;
                return slot;
            }

            if (slot.IsReal && TryCreateRealItem(slot))
                return slot;

            // Spells (an invisible rune goes into the hand, VRFavoritesRingSpell) - and a real item that failed.
            slot.Model = new GameObject("Model");
            slot.Model.transform.SetParent(root.transform, false);
            _vobService.CreateItemMesh(item.Index, slot.Model);
            foreach (var collider in slot.Model.GetComponentsInChildren<Collider>(true))
                Destroy(collider);
            // Runes and scrolls lie flat - stood up, their picture faces the player.
            slot.Model.transform.localRotation = Quaternion.Euler(_spellModelTilt, 0f, 0f);
            FitSize(slot.Model, RingConstants.SpellSize);
            return slot;
        }

        public void Remove(FavoritesRingSlotView slot)
        {
            if (slot.Container != null)
                _vobService.RemoveWorldItem(slot.Container);
            if (slot.Root != null)
                Destroy(slot.Root);
        }

        /// <summary>
        /// The weapon/item itself, kinematic and shrunk into the slot: a hand grabs it like out of a backpack socket
        /// (the backend's own grab, at the grip for weapons) - not from afar, without VRFocus's name (the ring shows
        /// its own).
        /// It doesn't touch the hands or the hero's body until grabbed.
        /// </summary>
        private bool TryCreateRealItem(FavoritesRingSlotView slot)
        {
            var container = _vobService.CreateItem(slot.InstanceName, 1);
            var grabbable = container?.Go != null ? container.Go.GetComponentInChildren<VrInteractable>() : null;
            if (grabbable == null)
            {
                if (container != null)
                    _vobService.RemoveWorldItem(container);
                Logger.LogWarning($"[FavoritesRing] {slot.InstanceName}: no grabbable item created", LogCat.VR);
                return false;
            }

            var rigidbody = grabbable.GetComponent<Rigidbody>();
            if (rigidbody != null)
                rigidbody.isKinematic = true;
            grabbable.IsDistanceGrabbable = false;
            slot.FocusName = container.Go.FindChildRecursively(_focusNameCanvas);

            var loader = container.Go.transform;
            slot.LoaderParent = loader.parent;
            slot.LoaderScale = loader.localScale;
            loader.SetParent(slot.Root.transform, false);
            loader.localPosition = Vector3.zero;
            loader.localRotation = Quaternion.identity;
            if (slot.IsWeapon)
            {
                StandUpright(container.Go);
                FitSize(container.Go, RingConstants.WeaponSize);
            }
            else
            {
                FitSize(container.Go, RingConstants.ItemSize);
            }

            slot.Container = container;
            slot.Grabbable = grabbable;
            slot.Model = container.Go;
            UpdateBodyCollisions(slot);
            return true;
        }

        /// <summary>
        /// The hands' and the hero's solid colliders ignore a ring item - also the colliders a weapon builds after
        /// being created (its fitted hitboxes). Once grabbed the backend handles the hands (and restores them on
        /// release), VRVobItem the body.
        /// </summary>
        public void UpdateBodyCollisions(FavoritesRingSlotView slot)
        {
            // A weapon builds its hitboxes after it was created - looked for now and then, not every frame.
            if (Time.time < slot.NextColliderCheckTime)
                return;
            slot.NextColliderCheckTime = Time.time + RingConstants.ColliderCheckSeconds;

            var itemColliders = _colliderBuffer;
            slot.Container.Go.GetComponentsInChildren(true, itemColliders);
            if (itemColliders.Count == slot.IgnoredColliderCount)
                return;
            slot.IgnoredColliderCount = itemColliders.Count;

            if (Rig == null)
                return;
            var bodyColliders = new List<Collider> { Rig.CharacterController };
            // Not the hands' triggers - the backend finds what a hand can grab with them.
            bodyColliders.AddRange(Rig.GetHandColliders(VrHandSide.Left));
            bodyColliders.AddRange(Rig.GetHandColliders(VrHandSide.Right));
            foreach (var bodyCollider in bodyColliders)
            {
                if (bodyCollider == null)
                    continue;
                foreach (var itemCollider in itemColliders)
                    Physics.IgnoreCollision(bodyCollider, itemCollider, true);
            }
        }

        private static Material CreateEmptySlotMaterial()
        {
            var material = new Material(Constants.ShaderUnlit);
            material.SetColor("_BaseColor", _emptySlotColor);
            return material;
        }

        /// <summary>
        /// A weapon's longest axis points up, its grip (the mesh's origin) down - spinning, it doesn't sweep through
        /// the neighbouring slots.
        /// </summary>
        private static void StandUpright(GameObject model)
        {
            model.transform.rotation = Quaternion.identity;
            if (!TryGetBounds(model, out var bounds))
                return;

            var size = bounds.size;
            var rotation = size.x >= size.y && size.x >= size.z ? Quaternion.Euler(0f, 0f, 90f)
                : size.z >= size.y ? Quaternion.Euler(-90f, 0f, 0f) : Quaternion.identity;
            model.transform.localRotation = rotation;

            if (TryGetBounds(model, out bounds) && model.transform.position.y > bounds.center.y)
                model.transform.localRotation = Quaternion.Euler(180f, 0f, 0f) * rotation;
        }

        /// <summary>
        /// Runes, scrolls, weapons and items have different sizes - each fills its slot size.
        /// </summary>
        private static void FitSize(GameObject model, float size)
        {
            if (!TryGetBounds(model, out var bounds))
                return;
            var largest = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            if (largest > 0.0001f)
                model.transform.localScale *= size / largest;

            // Centered on the slot (the mesh pivot isn't its center).
            if (TryGetBounds(model, out bounds))
                model.transform.position += model.transform.parent.position - bounds.center;
        }

        /// <summary>
        /// The meshes only - a weapon's swing trail or a bow's string reach to the world's origin.
        /// </summary>
        private static bool TryGetBounds(GameObject model, out Bounds bounds)
        {
            bounds = default;
            var renderers = model.GetComponentsInChildren<Renderer>(true)
                .Where(r => r is MeshRenderer or SkinnedMeshRenderer)
                .ToArray();
            if (renderers.Length == 0)
                return false;
            bounds = renderers[0].bounds;
            foreach (var renderer in renderers)
                bounds.Encapsulate(renderer.bounds);
            return true;
        }

        /// <summary>
        /// The spell's look: 0 the rune only, 1 its effect in front of the half transparent rune, 2 the effect half
        /// transparent while a hand is at it.
        /// </summary>
        public void SetPreview(FavoritesRingSlotView slot, int state)
        {
            slot.PreviewState = state;
            if (state > 0 && slot.Effect == null && !slot.HasNoEffect)
            {
                slot.Effect = CreateSpellEffect(slot.Item, slot.Root);
                slot.HasNoEffect = slot.Effect == null;
            }

            var isEffect = state > 0 && slot.Effect != null;
            if (slot.Effect != null)
                slot.Effect.SetActive(isEffect);
            if (slot.Model == null)
                return;

            slot.Model.SetActive(true);
            if (isEffect && state == 1)
                _dynamicMaterialService.SetDynamicValue(slot.Model, Constants.ShaderPropertyTransparency,
                    RingConstants.PreviewModelAlpha);
            else
                _dynamicMaterialService.ResetDynamicValue(slot.Model, Constants.ShaderPropertyTransparency,
                    Constants.ShaderPropertyTransparencyDefault);
            if (isEffect)
                SetEffectAlpha(slot, state == 2 ? RingConstants.PreviewEffectHoverAlpha : 1f);
        }

        /// <summary>
        /// The effect's own materials (instanced once) with their alpha scaled - particles blend by it.
        /// </summary>
        private static void SetEffectAlpha(FavoritesRingSlotView slot, float alpha)
        {
            if (slot.EffectMaterials == null)
            {
                slot.EffectMaterials = new List<(Material, Color)>();
                foreach (var renderer in slot.Effect.GetComponentsInChildren<Renderer>(true))
                {
                    foreach (var material in renderer.materials)
                    {
                        var property = material.HasProperty(_baseColorProperty) ? _baseColorProperty
                            : material.HasProperty(_colorProperty) ? _colorProperty : -1;
                        if (property < 0)
                            continue;
                        // The ring's own copies: no alpha test - at half alpha the particles (mostly below 50 %
                        // already) fell under its 0.5 cutoff and the whole effect vanished while a hand was at it.
                        material.DisableKeyword("_ALPHATEST_ON");
                        if (material.HasProperty(_cutoffProperty))
                            material.SetFloat(_cutoffProperty, 0f);
                        slot.EffectMaterials.Add((material, material.GetColor(property)));
                    }
                }
            }

            foreach (var (material, color) in slot.EffectMaterials)
            {
                if (material == null)
                    continue;
                var property = material.HasProperty(_baseColorProperty) ? _baseColorProperty : _colorProperty;
                material.SetColor(property, new Color(color.r, color.g, color.b, color.a * alpha));
            }
        }

        private GameObject CreateSpellEffect(ItemInstance item, GameObject parent)
        {
            // Gothic's motion, the whole effect fitted into the slot.
            return VRSpellEffect.Create(item, parent, _gameStateService.GothicVm, _vmCacheService, _meshService,
                true, RingConstants.SpellSize * RingConstants.EffectFitShare);
        }
    }
}
