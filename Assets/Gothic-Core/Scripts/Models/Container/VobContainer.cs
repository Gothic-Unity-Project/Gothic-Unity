using System;
using Gothic.Core.Adapters.Properties.Vobs;
using Gothic.Core.Services.Caches;
using Gothic.Core.Extensions;
using JetBrains.Annotations;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using ZenKit.Vobs;

namespace Gothic.Core.Models.Container
{
    public class VobContainer
    {
        public IVirtualObject Vob;

        /// <summary>
        /// Unity Properties which are needed at runtime, but aren't handled within ZenKit's VOB data (e.g., non-save relevant).
        /// </summary>
        public readonly VobProperties2 Props;
        public GameObject Go;

        /// <summary>
        /// Set for child VOBs which have an own VobLoader (e.g. an item lying on a mover). They're part of the world
        /// through their parent until they're taken (SaveGameService.DetachChildVob).
        /// </summary>
        [CanBeNull] public IVirtualObject ParentVob;

        /// <summary>
        /// Current state of a mob (Gothic's oCMobInter state), e.g. a wheel is turned, a door is open. Starts with 0,
        /// -1 while an NPC leaves it. Gothic's save format has no field for it (the VOB's State is stateNum, the number
        /// of states): MobStatesSaveSection saves it.
        /// </summary>
        public int MobState;


        [Inject] private readonly VmCacheService _vmCacheService;
        

        public VobContainer(IVirtualObject vob)
        {
            this.Inject();

            Vob = vob;
            
            switch (vob.Type)
            {
                case VirtualObjectType.zCVobSound:
                    Props = new SoundProperties(vob);
                    break;
                case VirtualObjectType.zCVobSoundDaytime:
                    Props = new SoundDayTimeProperties(vob);
                    break;
                case VirtualObjectType.oCZoneMusic:
                case VirtualObjectType.oCZoneMusicDefault:
                    Props = new MusicProperties(vob);
                    break;
                case VirtualObjectType.oCMobLadder:
                    Props = new LadderProperties(vob);
                    break;
                case VirtualObjectType.oCItem:
                    Props = new VobItemProperties2(vob);
                    break;
                case VirtualObjectType.oCMobFire:
                case VirtualObjectType.oCMobInter:
                    Props = new InteractiveProperties(vob);
                    break;
                case VirtualObjectType.oCMobBed:
                    Props = new BedProperties(vob);
                    break;
                case VirtualObjectType.oCMobDoor:
                    Props = new BedProperties(vob);
                    break;
                case VirtualObjectType.oCMobContainer:
                case VirtualObjectType.oCMobSwitch:
                    Props = new SwitchProperties(vob);
                    break;
                case VirtualObjectType.oCMobWheel:
                    Props = new WheelProperties(vob);
                    break;
                default:
                    Props = new VobProperties2(vob);
                    break;
            }
            
            Props.Init();
        }
        
        /// <summary>
        /// Shorthand function.
        /// </summary>
        public T VobAs<T>() where T : IVirtualObject
        {
            return (T)Vob;
        }

        /// <summary>
        /// Shorthand function.
        /// </summary>
        public T PropsAs<T>() where T : VobProperties2
        {
            return (T)Props;
        }

        /// <summary>
        /// Shorthand function.
        /// </summary>
        [CanBeNull]
        public ItemInstance GetItemInstance()
        {
            if (Vob.Type != VirtualObjectType.oCItem)
                return null;
            
            var vobItem = VobAs<IItem>();
            string itemName;
            if (!string.IsNullOrEmpty(vobItem.Instance))
                itemName = vobItem.Instance;
            else if (!string.IsNullOrEmpty(vobItem.Name))
                itemName = vobItem.Name;
            else
                throw new Exception("Vob Item -> no usable name found.");

            return _vmCacheService.TryGetItemData(itemName);
        }
    }
}
