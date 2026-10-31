using System.Collections;
using System.Collections.Generic;
using System.IO;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Meshes;
using Gothic.Core.Services.World;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;
using Object = UnityEngine.Object;

namespace Gothic.Core.Services.Vobs
{
    /// <summary>
    /// Torches (ITEM_TORCH) like the engine (OpenGothic Npc::toggleTorch, dropTorch, ItemTorchBurning): a lit torch
    /// becomes the burning torch item (ItLsTorchBurning) and shows its look (ITLSTORCHBURNING.ZEN: the flame, its
    /// light, its sound) at its tip. The item instance is the state, so a save keeps a lit torch burning. It keeps
    /// burning when dropped and lights the world around it. Water puts it out for good: it becomes a burnt torch
    /// (ItLsTorchBurned). Put away, it is a torch again - the inventory has no burning ones.
    /// </summary>
    public class TorchService
    {
        // Symbol names as Gothic writes them into a save.
        private const string _torchInstance = "ITLSTORCH";
        private const string _burningInstance = "ITLSTORCHBURNING";
        private const string _burnedInstance = "ITLSTORCHBURNED";
        private const string _fireName = "TorchFire";
        private const string _lightSound = "TORCH_ENLIGHT";
        // What the burning torch shows if its visual can't be read.
        private const string _defaultFire = "TORCH";
        private const float _defaultLightRange = 6f;
        private static readonly Color _defaultLightColor = new(1f, 0.7f, 0.4f);
        // The light follows the torch this often and once it moved this far (m) - each move re-gathers the renderers.
        private const float _followSeconds = 0.2f;
        private const float _minMove = 0.3f;
        // The flame is under water if a water surface is above it within this height (m).
        private const float _waterCheckHeight = 3f;

        [Inject] private readonly VmCacheService _vmCacheService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;
        [Inject] private readonly ContextGameVersionService _contextGameVersionService;
        [Inject] private readonly MeshService _meshService;
        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly StationaryLightsService _stationaryLightsService;
        [Inject] private readonly UnityMonoService _unityMonoService;

        private bool _isLookRead;
        private string _firePfx = _defaultFire;
        private float _lightRange = _defaultLightRange;
        private Color _lightColor = _defaultLightColor;
        private string _burnSound;

        public static bool IsTorch(ItemInstance item)
        {
            return item != null && (item.Flags & (int)VmGothicEnums.ItemFlags.ItemTorch) != 0;
        }

        public bool IsBurning(VobContainer torch)
        {
            return torch?.Vob is IItem && GetInstanceName(torch).EqualsIgnoreCase(_burningInstance);
        }

        /// <summary>
        /// The inventory holds no burning torches: a burning torch goes in as a torch (OpenGothic Npc::takeItem).
        /// </summary>
        public string GetInventoryInstance(string instanceName)
        {
            return instanceName.EqualsIgnoreCase(_burningInstance) ? _torchInstance : instanceName;
        }

        /// <summary>
        /// Lights an unlit torch, puts out a burning one. A burnt torch stays out.
        /// </summary>
        /// <returns>False if it isn't a torch or burnt out.</returns>
        public bool Toggle(VobContainer torch)
        {
            if (torch?.Go == null || torch.Vob is not IItem)
                return false;

            if (IsBurning(torch))
            {
                PutOut(torch, false);
                return true;
            }

            var item = _vmCacheService.TryGetItemData(GetInstanceName(torch));
            if (!IsTorch(item) || GetInstanceName(torch).EqualsIgnoreCase(_burnedInstance))
                return false;

            if (!SetInstance(torch, _burningInstance))
                return false;

            Light(torch, true);
            return true;
        }

        /// <summary>
        /// A burning torch item that comes into the world - a loaded game, a level change, a script - gets its fire.
        /// </summary>
        public void LightIfBurning(VobContainer vob)
        {
            if (IsBurning(vob) && vob.Go != null && FindFire(vob) == null)
                Light(vob, false);
        }

        /// <param name="isLitNow">The hero lit it just now: the lighting sound plays.</param>
        private void Light(VobContainer torch, bool isLitNow)
        {
            ReadBurningLook();

            // On the moving part of the item (its rigidbody), not its loader that stays where it was spawned.
            var body = GetBody(torch.Go);
            var fire = new GameObject(_fireName);
            fire.transform.SetParent(body.transform, false);
            fire.transform.localPosition = GetTipLocalPosition(body);
            _meshService.CreateVobPfx(_firePfx, Vector3.zero, Quaternion.identity, fire);

            if (!string.IsNullOrEmpty(_burnSound))
            {
                var clip = _audioService.GetRandomSoundClip(_burnSound);
                if (clip != null)
                {
                    var source = fire.AddComponent<AudioSource>();
                    source.clip = clip;
                    source.loop = true;
                    source.spatialBlend = 1f;
                    source.maxDistance = 15f;
                    source.Play();
                }
            }

            var lightClip = isLitNow ? _audioService.GetRandomSoundClip(_lightSound) : null;
            if (lightClip != null)
                AudioSource.PlayClipAtPoint(lightClip, fire.transform.position);

            var light = _stationaryLightsService.CreateMovingLight("Torch light", fire.transform.position,
                _lightRange, _lightColor);
            _unityMonoService.StartCoroutine(Burn(torch, fire, light));
            Logger.Log($"[Torch] {torch.Go.name} {(isLitNow ? "lit" : "burning")}.", LogCat.Vob);
        }

        /// <param name="isBurnedOut">Water put it out: it becomes a burnt torch.</param>
        private void PutOut(VobContainer torch, bool isBurnedOut)
        {
            var fire = FindFire(torch);
            if (fire != null)
                Object.Destroy(fire);

            SetInstance(torch, isBurnedOut ? _burnedInstance : _torchInstance);
            Logger.Log($"[Torch] {torch.Go?.name} {(isBurnedOut ? "burnt out in the water" : "put out")}.",
                LogCat.Vob);
        }

        /// <summary>
        /// The torch becomes another torch item. Name is the instance name too: the backpack and the focus read it.
        /// </summary>
        /// <returns>False if the scripts don't know the item.</returns>
        private bool SetInstance(VobContainer torch, string instanceName)
        {
            if (_vmCacheService.TryGetItemData(instanceName) == null)
            {
                Logger.LogWarning($"[Torch] The scripts have no {instanceName} - {torch.Go?.name} stays as it is.",
                    LogCat.Vob);
                return false;
            }

            var item = (IItem)torch.Vob;
            item.Instance = instanceName;
            item.Name = instanceName;
            torch.Props.Init();
            return true;
        }

        private static GameObject FindFire(VobContainer torch)
        {
            if (torch.Go == null)
                return null;

            var fire = GetBody(torch.Go).transform.Find(_fireName);
            return fire != null ? fire.gameObject : null;
        }

        /// <summary>
        /// The light follows the flame. The torch put out or into the inventory or the world unloaded ends the fire,
        /// water puts it out.
        /// </summary>
        private IEnumerator Burn(VobContainer torch, GameObject fire, GameObject light)
        {
            var wait = new WaitForSeconds(_followSeconds);
            var isInWater = false;
            while (fire != null && torch.Go != null)
            {
                var position = fire.transform.position;
                if (Physics.Raycast(position, Vector3.up, _waterCheckHeight, 1 << Constants.WaterLayer,
                        QueryTriggerInteraction.Collide))
                {
                    isInWater = true;
                    break;
                }

                if (light != null && (position - light.transform.position).sqrMagnitude >= _minMove * _minMove)
                    _stationaryLightsService.MoveLight(light, position);
                yield return wait;
            }

            // The light goes first, so it can't stay behind if turning the torch into a burnt one fails.
            if (light != null)
                Object.Destroy(light);
            if (isInWater)
                PutOut(torch, true);
        }

        /// <summary>
        /// The burning torch item's visual (ITLSTORCHBURNING.ZEN): the torch with a flame, a light and a sound as
        /// children.
        /// </summary>
        private void ReadBurningLook()
        {
            if (_isLookRead)
                return;
            _isLookRead = true;

            var visual = _vmCacheService.TryGetItemData(_burningInstance)?.Visual;
            if (string.IsNullOrEmpty(visual) || !visual.EndsWithIgnoreCase(".zen"))
            {
                Logger.LogWarning($"[Torch] {_burningInstance} has no .zen visual ({visual}) - torches burn with " +
                                  $"the default flame {_defaultFire}.", LogCat.Vob);
                return;
            }

            var world = _resourceCacheService.TryGetWorld(Path.GetFileNameWithoutExtension(visual),
                _contextGameVersionService.Version);
            if (world == null)
            {
                Logger.LogWarning($"[Torch] {visual} of {_burningInstance} not found - torches burn with the " +
                                  $"default flame {_defaultFire}.", LogCat.Vob);
                return;
            }

            foreach (var root in world.RootObjects)
                ReadChildren(root.Children);
            Logger.Log($"[Torch] Burning torch: flame {_firePfx}, light {_lightRange:F1} m, sound {_burnSound}.",
                LogCat.Vob);
        }

        private void ReadChildren(List<IVirtualObject> vobs)
        {
            foreach (var vob in vobs)
            {
                var visualName = vob.Visual?.Name;
                if (visualName != null && visualName.EndsWithIgnoreCase(".pfx"))
                    _firePfx = Path.GetFileNameWithoutExtension(visualName);
                else if (vob is ZenKit.Vobs.Light light && light.Range > 0f)
                {
                    _lightRange = light.Range * .01f;
                    _lightColor = new Color(light.Color.R / 255f, light.Color.G / 255f, light.Color.B / 255f);
                }
                else if (vob is ZenKit.Vobs.Sound sound && !string.IsNullOrEmpty(sound.SoundName))
                    _burnSound = Path.GetFileNameWithoutExtension(sound.SoundName);

                ReadChildren(vob.Children);
            }
        }

        private static string GetInstanceName(VobContainer torch)
        {
            var item = (IItem)torch.Vob;
            return !string.IsNullOrEmpty(item.Instance) ? item.Instance : item.Name;
        }

        private static GameObject GetBody(GameObject torch)
        {
            var body = torch.GetComponentInChildren<Rigidbody>();
            return body != null ? body.gameObject : torch;
        }

        /// <summary>
        /// The end of the torch farther from its grip (the item's origin) along its longest axis.
        /// </summary>
        private static Vector3 GetTipLocalPosition(GameObject torch)
        {
            var meshFilter = torch.GetComponentInChildren<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
                return Vector3.up * 0.4f;

            var bounds = meshFilter.sharedMesh.bounds;
            var size = bounds.size;
            var axis = size.x >= size.y && size.x >= size.z ? Vector3.right
                : size.y >= size.z ? Vector3.up
                : Vector3.forward;
            var top = bounds.center + Vector3.Scale(bounds.extents, axis);
            var bottom = bounds.center - Vector3.Scale(bounds.extents, axis);
            var tip = top.sqrMagnitude >= bottom.sqrMagnitude ? top : bottom;
            return torch.transform.InverseTransformPoint(meshFilter.transform.TransformPoint(tip));
        }
    }
}
