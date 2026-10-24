using Gothic.Core.Adapters.Vob;
using Gothic.Core.Models.Container;
using System.Collections.Generic;
using Gothic.Core.Models.Vm;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Services;
using Reflex.Attributes;
using UnityEngine;

namespace Gothic.VR.Adapters.Vob.Container
{
    /// <summary>
    /// Handle Socket-events for a whole Container (e.g., chest) and its corresponding sockets (rings where we put items into).
    /// Plays the weapon (un)draw sound when a weapon is put into or taken out of a socket.
    /// </summary>
    public class VRVobContainerSocketInventory : MonoBehaviour
    {
        [Inject] private VRWeaponService _vrWeaponService;

        [Tooltip("Sockets outside of this GameObject's hierarchy (e.g. the player's holsters). " +
                 "Sockets below this GameObject are found automatically.")]
        [SerializeField] private GameObject[] _additionalSockets;

        private readonly List<IVrSocket> _sockets = new();

        private void Awake()
        {
            _sockets.AddRange(GetComponentsInChildren<IVrSocket>(true));
            foreach (var socketGo in _additionalSockets)
            {
                if (socketGo != null && socketGo.TryGetComponent(out IVrSocket socket))
                    _sockets.Add(socket);
            }

            foreach (var socket in _sockets)
            {
                socket.BeforeGrabbed += OnBeforeGrabbed;
                socket.Released += OnReleased;
            }
        }

        private void OnDestroy()
        {
            foreach (var socket in _sockets)
            {
                socket.BeforeGrabbed -= OnBeforeGrabbed;
                socket.Released -= OnReleased;
            }
        }

        private void OnBeforeGrabbed(VrGrabArgs args)
        {
            if (args.Interactable == null)
                return;

            var container = args.Interactable.GetComponentInParent<VobLoader>()?.Container;
            if (!IsWeapon(container))
                return;
            _vrWeaponService.PlayUndrawSound(container);
        }

        private void OnReleased(VrGrabArgs args)
        {
            if (args.Interactable == null)
                return;

            var container = args.Interactable.GetComponentInParent<VobLoader>()?.Container;
            if (!IsWeapon(container))
                return;
            _vrWeaponService.PlayDrawSound(container);
        }

        private bool IsWeapon(VobContainer container)
        {
            var itemInstance = container?.GetItemInstance();
            if (itemInstance == null) return false;
            var flag = (VmGothicEnums.ItemFlags)itemInstance.MainFlag;
            return flag is VmGothicEnums.ItemFlags.ItemKatNf or VmGothicEnums.ItemFlags.ItemKatFf;
        }
    }
}
