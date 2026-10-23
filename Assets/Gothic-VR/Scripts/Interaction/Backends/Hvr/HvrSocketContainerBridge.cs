#if GOTHIC_HVR_INSTALLED
using System.Collections.Generic;
using Gothic.Core.Logging;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Ports;
using HurricaneVR.Framework.Core;
using HurricaneVR.Framework.Core.Sockets;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Interaction.Backends.Hvr
{
    /// <summary>
    /// Sits next to an HVRSocketContainer. Every socket of the container needs an HvrSocketBridge.
    /// </summary>
    public class HvrSocketContainerBridge : MonoBehaviour, IVrSocketContainer
    {
        [Inject] private readonly IVrBackend _vrBackend;

        private HVRSocketContainer _container;
        private readonly Dictionary<IVrSocket, (AudioClip Placed, AudioClip Removed)> _savedSounds = new();
        private bool _isSilent;

        private HVRSocketContainer Container
        {
            get
            {
                if (_container == null)
                    _container = GetComponent<HVRSocketContainer>();
                return _container;
            }
        }

        public bool IsActive => _vrBackend == null || _vrBackend is HvrBackend;
        public bool IsSilent => _isSilent;

        public IReadOnlyList<IVrSocket> Sockets
        {
            get
            {
                var result = new List<IVrSocket>(Container.Sockets.Count);
                foreach (var hvrSocket in Container.Sockets)
                {
                    var socket = GetSocket(hvrSocket);
                    if (socket != null)
                        result.Add(socket);
                }

                return result;
            }
        }

        public bool TryFindFree(VrInteractable interactable, out IVrSocket socket)
        {
            socket = null;
            if (interactable == null || !interactable.TryGetComponent(out HVRGrabbable grabbable))
                return false;
            if (!Container.TryFindAvailableSocket(grabbable, out var hvrSocket))
                return false;

            socket = GetSocket(hvrSocket);
            return socket != null;
        }

        public bool TryPlace(VrInteractable interactable, bool silent = false)
        {
            if (interactable == null || !interactable.TryGetComponent(out HVRGrabbable grabbable))
                return false;
            if (!silent)
                return Container.TryAddGrabbable(grabbable);

            return TryFindFree(interactable, out var socket) && socket.TryPlace(interactable, true);
        }

        /// <summary>
        /// HVR plays the sockets' fallback clips, so they're removed while silent and restored afterwards.
        /// </summary>
        public void SetSilent(bool isSilent)
        {
            if (_isSilent == isSilent)
                return;

            _isSilent = isSilent;
            foreach (var socket in Sockets)
            {
                if (isSilent)
                {
                    _savedSounds[socket] = (socket.PlacedSoundFallback, socket.RemovedSoundFallback);
                    socket.PlacedSoundFallback = null;
                    socket.RemovedSoundFallback = null;
                }
                else if (_savedSounds.TryGetValue(socket, out var sounds))
                {
                    socket.PlacedSoundFallback = sounds.Placed;
                    socket.RemovedSoundFallback = sounds.Removed;
                }
            }

            if (!isSilent)
                _savedSounds.Clear();
        }

        private static IVrSocket GetSocket(Component hvrSocket)
        {
            if (hvrSocket == null)
                return null;

            var socket = hvrSocket.GetComponent<IVrSocket>();
            if (socket == null)
                Logger.LogError($"[HVR] Socket {hvrSocket.name} has no HvrSocketBridge.", LogCat.VR);
            return socket;
        }
    }
}
#endif
