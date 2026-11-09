using System.Collections.Generic;
using Gothic.Core.Models.Container;
using Gothic.VR.Interaction.Components;
using UnityEngine;
using ZenKit.Daedalus;

namespace Gothic.VR.Models.FavoritesRing
{
    /// <summary>
    /// A favorites ring slot as VRFavoritesRing shows it: its content (FavoriteSlot) and its GameObjects.
    /// </summary>
    public class FavoritesRingSlotView
    {
        public FavoriteSlot Content;
        public GameObject Root;
        public GameObject Model;
        // VRFocus's hover name of a real item.
        public GameObject FocusName;
        public GameObject Effect;
        public bool HasNoEffect;
        // The materials of its effect with their own alpha, and the
        // look shown (-1 none yet, 0 rune only, 1 effect in front, 2 effect while hovered).
        public List<(Material Material, Color Color)> EffectMaterials;
        public int PreviewState = -1;
        public float Angle;

        // Weapons and items: the real item, shrunk into the slot until a hand grabs it.
        public VobContainer Container;
        public VrInteractable Grabbable;
        public Transform LoaderParent;
        public Vector3 LoaderScale;
        public int IgnoredColliderCount;
        public float NextColliderCheckTime;
        public float TakenTime = -1f;

        public FavoriteSlotKind Kind => Content.Kind;
        public ItemInstance Item => Content.Item;
        public string InstanceName => Content.InstanceName;
        public int Amount => Content.Amount;
        public bool IsReal => Content.IsReal;
        public bool IsWeapon => Content.IsWeapon;
    }
}
