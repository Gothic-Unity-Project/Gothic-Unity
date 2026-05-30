using System;
using GUZ.Core.Adapters.Adnimations;
using GUZ.Core.Adapters.Animations.Morph;
using GUZ.Core.Animations;
using GUZ.Core.Models.Container;
using GUZ.Core.Extensions;
using UnityEngine;
using ZenKit.Daedalus;

namespace GUZ.Core.Adapters.Npc
{
    public class NpcPrefabProperties : MonoBehaviour
    {
        // Hero has different behaviour. e.g. no AIHandler attached and we therefore set the data from above (CacheHero())
        [SerializeField]
        private bool _isHero;

        // Values set via Inspector
        public AudioSource NpcSound;
        public Transform Bip01;
        public Transform ColliderRootMotion;
        public AiHandler AiHandler;
        public Animation Animation;
        public AnimationSystem AnimationSystem;
        public NpcHeadAnimationHandler AnimationHeadHandler;
        public INpcSubtitles NpcSubtitles;

        [NonSerialized]
        public Transform Head;
        [NonSerialized]
        public HeadMorph HeadMorph;

        [NonSerialized]
        public VobContainer CurrentInteractable; // e.g. PSI_CAULDRON
        [NonSerialized]
        public GameObject CurrentInteractableSlot; // e.g. ZS_0


        private string _focusName;

        private void Awake()
        {
            if (_isHero)
            {
                return;
            }

            // else
            var npcContainer = GetComponentInParent<NpcLoader>().Npc.GetUserData();
            npcContainer.PrefabProps = this;
            _focusName = npcContainer.Instance.GetName(NpcNameSlot.Slot0);
        }

        public string GetFocusName()
        {
            return _focusName;
        }

        public bool IsHero()
        {
            return _isHero;
        }
    }
}
