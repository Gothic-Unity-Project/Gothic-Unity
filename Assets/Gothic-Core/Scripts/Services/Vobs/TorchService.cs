using System.Collections;
using System.Collections.Generic;
using System.IO;
using Gothic.Core.Const;
using Gothic.Core.Domain.Vobs;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Vm;
using Gothic.Core.Models.Vob;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Meshes;
using Gothic.Core.Services.World;
using JetBrains.Annotations;
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
    /// The torch light option (off by default): the burning torch in the player's hand lights the world as the hero
    /// light (HeroLightService) - sharp, by the angle of each surface, following the flame every frame - and its flame
    /// glows. Its normal light is off meanwhile and comes back when the torch leaves the hand.
    /// </summary>
    public class TorchService
    {
        // The torch light option in Gothic.ini: 0 = off, 1 = on.
        public const string IniSection = "GAME";
        public const string IniKeyHeroLight = "torchHeroLight";
        public const int HeroLightDefaultValue = 0;

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
        // The torch light's lensflareFX (TORCHFX01) names an effect in LENSFLARE.ZEN, which isn't read yet: the flame
        // glows with this texture of Gothic's.
        private const string _fallbackGlowTexture = "SIMPLEGLOW.TGA";
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
        [Inject] private readonly HeroLightService _heroLightService;
        [Inject] private readonly ConfigService _configService;

        private bool _isLookRead;
        private string _firePfx = _defaultFire;
        private LightProfile _lightProfile = new(_defaultLightRange, _defaultLightColor);
        private string _burnSound;

        private readonly Dictionary<VobContainer, BurningTorch> _burningTorches = new();
        private LensFlareDomain _lensFlareDomain;
        // The items in the hero's hands (VR reports them) and the one grabbed last.
        private VobContainer _heldLeft;
        private VobContainer _heldRight;
        private VobContainer _lastGrabbed;
        // The burning torch that lights the world as the hero light - only UpdateHeroLight() changes it.
        private BurningTorch _heroTorch;
        // The torch light option, read from Gothic.ini when first needed.
        private bool? _isHeroLightOn;

        public TorchService()
        {
            GlobalEventDispatcher.PlayerPrefUpdated.AddListener(OnSettingChanged);
            GlobalEventDispatcher.LoadingSceneLoaded.AddListener(OnWorldUnloaded);
        }

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
            RiseInWorld(fire);

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
                _lightProfile.Range, _lightProfile.Color);
            var burning = new BurningTorch(torch, fire, light);
            _burningTorches[torch] = burning;
            _unityMonoService.StartCoroutine(Burn(burning));
            Logger.Log($"[Torch] {torch.Go.name} {(isLitNow ? "lit" : "burning")}.", LogCat.Vob);

            // Lit in the hand: it becomes the hero light right away.
            UpdateHeroLight();
        }

        /// <param name="isBurnedOut">Water put it out: it becomes a burnt torch.</param>
        private void PutOut(VobContainer torch, bool isBurnedOut)
        {
            var fire = FindFire(torch);
            if (fire != null)
                Object.Destroy(fire);
            _burningTorches.Remove(torch);

            SetInstance(torch, isBurnedOut ? _burnedInstance : _torchInstance);
            Logger.Log($"[Torch] {torch.Go?.name} {(isBurnedOut ? "burnt out in the water" : "put out")}.",
                LogCat.Vob);
            UpdateHeroLight();
        }

        /// <summary>
        /// The items in the hero's hands (null: an empty hand), reported when they change. A burning torch among them
        /// lights the world as the hero light - the one grabbed last if both hands hold one.
        /// </summary>
        public void SetHeldTorches(VobContainer left, VobContainer right)
        {
            if (left != null && left != _heldLeft)
                _lastGrabbed = left;
            if (right != null && right != _heldRight)
                _lastGrabbed = right;
            _heldLeft = left;
            _heldRight = right;
            UpdateHeroLight();
        }

        /// <summary>
        /// The one place that decides the hero torch, called whenever something it depends on changes: the torches
        /// in the hands, a torch lit, put out or gone, the option, the world. Nothing happens if it stays the same.
        /// </summary>
        private void UpdateHeroLight()
        {
            var hero = IsHeroLightOn ? FindHeroTorch() : null;
            if (hero == _heroTorch)
                return;

            var previous = _heroTorch;
            _heroTorch = hero;

            // First the normal light comes back, then the hero light fades out - no dark moment in between.
            if (previous != null && IsStillBurning(previous))
                HandBack(previous);

            if (hero == null)
            {
                _heroLightService.Hide(this);
                return;
            }

            // The hero light takes over: the normal light would light the same spots a second time.
            if (hero.Light != null)
                hero.Light.SetActive(false);
            if (hero.Glow == null)
                hero.Glow = CreateGlow(hero.Fire);
            if (hero.Glow != null)
                hero.Glow.SetActive(true);

            _heroLightService.Show(this, hero.Fire.transform, _lightProfile);
            Logger.Log($"[Torch] {hero.Torch.Go.name} lights the world as the hero light.", LogCat.Vob);
        }

        /// <summary>
        /// A burning torch in a hand, the one grabbed last first.
        /// </summary>
        private BurningTorch FindHeroTorch()
        {
            var isLastGrabbedHeld = _lastGrabbed != null && (_lastGrabbed == _heldLeft || _lastGrabbed == _heldRight);
            return (isLastGrabbedHeld ? GetBurning(_lastGrabbed) : null) ?? GetBurning(_heldLeft) ??
                GetBurning(_heldRight);
        }

        private BurningTorch GetBurning(VobContainer torch)
        {
            return torch?.Go != null && _burningTorches.TryGetValue(torch, out var burning) && burning.Fire != null
                ? burning
                : null;
        }

        private bool IsStillBurning(BurningTorch burning)
        {
            return _burningTorches.TryGetValue(burning.Torch, out var current) && current == burning &&
                   burning.Fire != null;
        }

        /// <summary>
        /// The torch left the hand: its normal light comes back where the flame is now, its glow goes.
        /// </summary>
        private void HandBack(BurningTorch burning)
        {
            if (burning.Glow != null)
                burning.Glow.SetActive(false);
            if (burning.Light == null)
                return;

            _stationaryLightsService.MoveLight(burning.Light, burning.Fire.transform.position);
            burning.Light.SetActive(true);
        }

        [CanBeNull]
        private GameObject CreateGlow(GameObject fire)
        {
            _lensFlareDomain ??= new LensFlareDomain().Inject();
            return _lensFlareDomain.Create(fire.transform, _fallbackGlowTexture, _lightProfile.Color);
        }

        private bool IsHeroLightOn
        {
            get
            {
                if (_isHeroLightOn == null && _configService.Gothic != null)
                    _isHeroLightOn = _configService.Gothic.GetInt(IniKeyHeroLight, HeroLightDefaultValue) != 0;
                return _isHeroLightOn == true;
            }
        }

        private void OnSettingChanged(string key, object value)
        {
            if (key != IniKeyHeroLight)
                return;

            _isHeroLightOn = null;
            Logger.Log($"[Torch] Torch light {(IsHeroLightOn ? "on" : "off")}.", LogCat.Vob);
            UpdateHeroLight();
        }

        /// <summary>
        /// The torches went with the old world.
        /// </summary>
        private void OnWorldUnloaded()
        {
            _burningTorches.Clear();
            _heldLeft = null;
            _heldRight = null;
            _lastGrabbed = null;
            UpdateHeroLight();
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
        /// The light follows the flame - not while the torch is the hero light, which follows it itself. The torch put
        /// out or into the inventory or the world unloaded ends the fire, water puts it out.
        /// </summary>
        private IEnumerator Burn(BurningTorch burning)
        {
            var torch = burning.Torch;
            var fire = burning.Fire;
            var light = burning.Light;
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

                if (burning != _heroTorch && light != null &&
                    (position - light.transform.position).sqrMagnitude >= _minMove * _minMove)
                    _stationaryLightsService.MoveLight(light, position);
                yield return wait;
            }

            // The light goes first, so it can't stay behind if turning the torch into a burnt one fails.
            if (light != null)
                Object.Destroy(light);
            if (_burningTorches.TryGetValue(torch, out var current) && current == burning)
                _burningTorches.Remove(torch);
            UpdateHeroLight();
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
            Logger.Log($"[Torch] Burning torch: flame {_firePfx}, light {_lightProfile.Range:F1} m (lens flare " +
                       $"{_lightProfile.LensFlareName}), sound {_burnSound}.", LogCat.Vob);
        }

        private void ReadChildren(List<IVirtualObject> vobs)
        {
            foreach (var vob in vobs)
            {
                var visualName = vob.Visual?.Name;
                if (visualName != null && visualName.EndsWithIgnoreCase(".pfx"))
                    _firePfx = Path.GetFileNameWithoutExtension(visualName);
                else if (vob is ZenKit.Vobs.Light light && light.Range > 0f)
                    _lightProfile = light.ToLightProfile();
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

        /// <summary>
        /// The flame rises to the sky however the torch is held: its particles fly in world space and are only emitted
        /// at the tip. TORCH names no frame for its up direction, so it was the torch's own - a tilted VR torch blew
        /// its flame sideways.
        /// </summary>
        private static void RiseInWorld(GameObject fire)
        {
            foreach (var particles in fire.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = particles.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                var velocity = particles.velocityOverLifetime;
                velocity.space = ParticleSystemSimulationSpace.World;
                var force = particles.forceOverLifetime;
                force.space = ParticleSystemSimulationSpace.World;
            }
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
