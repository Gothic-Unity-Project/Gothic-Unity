#if GOTHIC_HVR_INSTALLED
using System.Collections;
using Gothic.Core.Domain.Inventory;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Services.Config;
using Gothic.VR.Adapters.Player;
using HurricaneVR.Framework.Core;
using Reflex.Attributes;
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
        [Inject] private readonly ConfigService _configService;

        private const float _backpackDistance = 0.8f;
        private const float _backpackHeight = 0.5f;

        private IContainer _container;
        private VRBackpack _lootBackpack;
        private GameObject _lootBackpackRoot;
        private Coroutine _timeout;

        // Added at runtime - after the GameObject's own injection ran.
        private void Awake() => this.Inject();

        public void Toggle(IContainer container)
        {
            _container = container;

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
            // On the hero's side of the VOB - its own forward can point into the wall (the mummies stand with their
            // back... or front... to it).
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

            _lootBackpack.SetContainerOwner(new ContainerInventoryOwner(_container));

            // Lying untouched for a while: gone. Held, it stays; let go, the time starts again.
            var backpackGrabbable = _lootBackpack.GetComponent<HVRGrabbable>();
            if (backpackGrabbable != null)
            {
                backpackGrabbable.Grabbed.AddListener((_, _) => StopTimeout());
                backpackGrabbable.Released.AddListener((_, _) => StartTimeout());
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
            yield return new WaitForSeconds(Mathf.Max(1f, _configService.Dev.NpcLootBackpackTimeout));
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
#endif
