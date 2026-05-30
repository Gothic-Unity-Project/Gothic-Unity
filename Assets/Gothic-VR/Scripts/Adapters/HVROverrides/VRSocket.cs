#if GUZ_HVR_INSTALLED
using System;
using GUZ.Core.Adapters.Vob;
using GUZ.Core.Extensions;
using GUZ.Core.Models.Vm;
using HurricaneVR.Framework.Core;
using HurricaneVR.Framework.Core.Grabbers;
using UnityEngine;
using ZenKit.Daedalus;

namespace GUZ.VR.Adapters.HVROverrides
{
    /// <summary>
    /// Our VobItems have the following structure:
    /// |- VobLoader.comp
    /// |-- Mesh + Grabbable.comp
    /// |--- ...
    /// Problem is that HVR assumes the root of an object to be moved is the Grabbable GO. But on our end it's one level higher.
    /// This class therefore overwrites some logic to ensure, that our root with VobLoader (which will be used by e.g. Cullung) is the root.
    /// </summary>
    public class VRSocket : HVRSocket
    {
        protected override void OnGrabbed(HVRGrabArgs args)
        {
            // HINT: We can't call base.OnGrabbed(), as it would break the parent behaviour already. We therefore recreate its logic here.

            // From HVRGrabberBase.cs
            {
                args.Grabbable.Destroyed.AddListener(OnGrabbableDestroyed);
            }

            // From HVRSocket.cs (change: _previousParent variable is a different one)
            // Structure: Bucket -> VobLoader -> Grabbable. We therefore need to put the VobLoader to another spot.
            {
                var grabbable = args.Grabbable;
                var vobLoader = grabbable.GetComponentInParent<VobLoader>(true); // e.g., Backpack item might be disabled already as it's re-parented in parallel.
                _previousParent = vobLoader.transform.parent; // We use parent of Grabbable object.
                _previousScale = grabbable.transform.localScale;

                AttachGrabbable(grabbable);
                OnGrabbableParented(grabbable);
                HandleRigidBodyGrab(grabbable);
                PlaySocketedSFX(grabbable.Socketable);

                if (args.RaiseEvents)
                {
                    Grabbed.Invoke(this, grabbable);
                }
            }
        }

        /// <summary>
        /// base.OnReleased() will set the Grabbable.comp GO to oCItem root. But this would ignore our VobLoader.comp GO in between.
        /// We therefore reset it to the named structure from this class headers documentation again.
        /// </summary>
        protected override void OnReleased(HVRGrabbable grabbable)
        {
            var tmpPreviousParent = _previousParent;
            var itemRoot = grabbable.GetComponentInParent<VobLoader>(true).transform;

            base.OnReleased(grabbable);
            
            grabbable.transform.parent = itemRoot;
            itemRoot.parent = tmpPreviousParent;
        }

        protected override void AttachGrabbable(HVRGrabbable grabbable)
        {
            var vobLoader = grabbable.GetComponentInParent<VobLoader>(true);
            // Structure: Bucket -> VobLoader -> Grabbable. We therefore need to put the VobLoader to another spot.
            vobLoader.gameObject.SetParent(transform.gameObject, resetLocation: true, resetRotation: true);
            
            RotateItemForCategorySlot(vobLoader.gameObject, vobLoader.Container.GetItemInstance());
        }

        private void RotateItemForCategorySlot(GameObject rootGo, ItemInstance item)
        {
            switch ((VmGothicEnums.ItemFlags)item.MainFlag)
            {
                case VmGothicEnums.ItemFlags.ItemKatNf:
                case VmGothicEnums.ItemFlags.ItemKatMun:
                    rootGo.transform.localRotation = Quaternion.Euler(-45, 0, -90);
                    break;
                case VmGothicEnums.ItemFlags.ItemKatFf:
                    rootGo.transform.localRotation = Quaternion.Euler(45, 180, -90);
                    break;
                case VmGothicEnums.ItemFlags.ItemKatArmor:
                    rootGo.transform.localRotation = Quaternion.Euler(0, 90, 0);
                    break;
                case VmGothicEnums.ItemFlags.ItemKatFood:
                    rootGo.transform.localRotation = Quaternion.Euler(0, 0, 22.5f);
                    break;
                case VmGothicEnums.ItemFlags.ItemKatDocs:
                    rootGo.transform.localRotation = Quaternion.Euler(90, 0, 90);
                    break;
                case VmGothicEnums.ItemFlags.ItemKatRune:
                    // Runes and magic scrolls
                    rootGo.transform.localRotation = Quaternion.Euler(-90, 0, 90);
                    break;
                case VmGothicEnums.ItemFlags.ItemKatMagic:
                    // Rings and amulets
                    rootGo.transform.localRotation = Quaternion.Euler(0, 0, 45);
                    break;
                case VmGothicEnums.ItemFlags.ItemKatPotions:
                    // 0, 0, 0 - nothing to rotate.
                    break;
                default:
                    // Misc and all other we missed.
                    rootGo.transform.localRotation = Quaternion.Euler(-45, 135, -45);
                    break;
            }
        }
    }
}
#endif
