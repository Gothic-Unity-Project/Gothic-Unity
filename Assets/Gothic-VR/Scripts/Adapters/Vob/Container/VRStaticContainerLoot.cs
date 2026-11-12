using System.Collections;
using Gothic.Core.Logging;
using Gothic.Core.Models.Inventory;
using Gothic.VR.Adapters.Player;
using Gothic.VR.Interaction.Components;
using UnityEngine;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Vob.Container
{
    /// <summary>
    /// An oCMobContainer without a lid (no .MDS - e.g. the orc mummies in the Sleeper's temple) is a normal mob
    /// (highlight + click). The click shows a tinted backpack with its items next to it, a second click hides it -
    /// like looting a corpse. Taken items leave IContainer.Contents, so they're saved with the world.
    /// </summary>
    public class VRStaticContainerLoot : MonoBehaviour
    {
        private const float _backpackDistance = 0.8f;
        private const float _backpackHeight = 0.5f;
        private const float _timeoutSeconds = 20f;

        private IContainer _container;
        private IInventoryOwner _owner;
        private VRBackpack _lootBackpack;
        private GameObject _lootBackpackRoot;
        private Coroutine _timeout;

        public void Toggle(IContainer container, IInventoryOwner owner)
        {
            _container = container;
            _owner = owner;

            if (_lootBackpack != null)
            {
                Close();
                return;
            }

            if (_container.IsLocked)
            {
                Logger.LogWarning($"[VRStaticContainerLoot] '{_container.Name}' is locked - no lock picking without a lid.",
                    LogCat.VR);
                return;
            }

            Open();
        }

        private void Open()
        {
            // On the hero's side of the VOB - its own forward can point into the wall.
            var toHero = transform.forward;
            var cam = Camera.main;
            if (cam != null)
            {
                var flat = cam.transform.position - transform.position;
                flat.y = 0f;
                if (flat.sqrMagnitude > 0.01f)
                    toHero = flat.normalized;
            }

            var position = transform.position + toHero * _backpackDistance + Vector3.up * _backpackHeight;
            _lootBackpack = VRBackpack.SpawnCopy(position, Quaternion.LookRotation(-toHero),
                $"LootBackpack_{_container.Name}", out _lootBackpackRoot);
            if (_lootBackpack == null)
                return;

            _lootBackpack.SetContainerOwner(_owner);

            // Lying untouched for a while: gone. Held, it stays; let go, the time starts again.
            var backpackInteractable = VrInteractable.Find(_lootBackpack);
            if (backpackInteractable != null)
            {
                backpackInteractable.Grabbed += _ => StopTimeout();
                backpackInteractable.Released += _ => StartTimeout();
            }
            StartTimeout();
            Logger.Log($"[VRStaticContainerLoot] '{_container.Name}' opened ({_container.Contents})", LogCat.VR);
        }

        private void Close()
        {
            StopTimeout();
            if (_lootBackpack != null)
                _lootBackpack.Despawn(_lootBackpackRoot);
            else if (_lootBackpackRoot != null)
                Destroy(_lootBackpackRoot);
            _lootBackpack = null;
            _lootBackpackRoot = null;
        }

        private void StartTimeout()
        {
            StopTimeout();
            // Despawn force-releases a held backpack - that Released comes after Close(), no new timer.
            if (_lootBackpack == null)
                return;
            if (isActiveAndEnabled)
                _timeout = StartCoroutine(CloseWhenUnused());
        }

        private void StopTimeout()
        {
            if (_timeout == null)
                return;
            StopCoroutine(_timeout);
            _timeout = null;
        }

        private IEnumerator CloseWhenUnused()
        {
            yield return new WaitForSeconds(_timeoutSeconds);
            _timeout = null;
            Logger.Log($"[VRStaticContainerLoot] '{_container.Name}' loot not picked up - closed", LogCat.VR);
            Close();
        }

        // Culled while open: no coroutines anymore - don't leave the backpack lying around.
        private void OnDisable()
        {
            Close();
        }
    }
}
