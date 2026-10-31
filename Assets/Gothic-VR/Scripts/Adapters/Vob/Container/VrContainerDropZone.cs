using Gothic.Core.Logging;
using Gothic.VR.Interaction.Components;
using Gothic.VR.Interaction.Ports;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Vob.Container
{
    /// <summary>
    /// Drop target of a chest. An item released by a hand while it's inside the drop zone is put
    /// into a free slot of the container. Sits on the GameObject of the VrDropZone (trigger collider) or a parent.
    /// Contacts and the placement one frame after the release are handled by VrDropZone and VrDropZoneService.
    /// </summary>
    public class VrContainerDropZone : MonoBehaviour, IVrDropTarget
    {
        [Tooltip("GameObject with the IVrSocketContainer (e.g. VrSocketContainer). Empty: this GameObject or its " +
                 "parents.")]
        [SerializeField] private GameObject _containerGo;

        private IVrSocketContainer _container;

        public IVrSocketContainer Container
        {
            get
            {
                if (_container == null)
                    _container = _containerGo != null
                        ? _containerGo.GetComponent<IVrSocketContainer>()
                        : GetComponentInParent<IVrSocketContainer>(true);
                return _container;
            }
        }

        private void Awake()
        {
            if (Container == null)
                Logger.LogWarning($"[Socket] Drop zone {name} has no IVrSocketContainer.", LogCat.VR);
        }

        public bool CanTake(VrInteractable item)
        {
            return Container != null && VrSocketSlot.IsItem(item);
        }

        public bool TryTake(VrInteractable item)
        {
            if (Container == null)
                return false;

            if (Container.TryPlace(item))
                return true;

            Logger.Log($"[Socket] No free slot for {item.name} in {name}.", LogCat.VR);
            return false;
        }
    }
}
