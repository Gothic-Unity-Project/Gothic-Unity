#if GUZ_HVR_INSTALLED
using System.Collections.Generic;
using System.Linq;
using GUZ.Core;
using GUZ.Core.Adapters.Vob;
using GUZ.Core.Extensions;
using GUZ.Core.Manager;
using GUZ.Core.Services.Caches;
using HurricaneVR.Framework.Components;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Vobs;

namespace GUZ.VR.Adapters.Vob
{
    public class VRVobDoor : MonoBehaviour
    {
        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;

        
        private static Dictionary<string, AudioClip> _doorOpenedClips = new();
        private static Dictionary<string, AudioClip> _doorClosedClips= new();
        
        private void Start()
        {
            var vobDoor = GetComponentInParent<VobLoader>().Container.VobAs<IDoor>();
            PrepareSounds(vobDoor);
        }
        
        /// <summary>
        /// Add opening and closing sound to the containers HVR settings.
        /// The logic about when to play them is provided by HVR itself.
        /// </summary>
        private void PrepareSounds(IDoor props)
        {
            if (props == null)
            {
                return;
            }

            var mdsName = props.Visual?.Name;

            if (string.IsNullOrEmpty(mdsName))
            {
                return;
            }

            // Check if the sound is already loaded and cached.
            
            // If the sound isn't already loaded and cached: Do it now.
            if (!_doorClosedClips.ContainsKey(mdsName.ToLower()))
            {
                var mds = _resourceCacheService.TryGetModelScript(mdsName);

                if (mds == null)
                {
                    return;
                }

                var openSoundEffect = mds.Animations.First(i => i.Name.EqualsIgnoreCase("t_S0_2_S1")).SoundEffects.First().Name;
                var closeSoundEffect = mds.Animations.First(i => i.Name.EqualsIgnoreCase("t_S1_2_S0")).SoundEffects.First().Name;

                _doorOpenedClips.Add(mdsName.ToLower(), _audioService.GetRandomSoundClip(openSoundEffect));
                _doorClosedClips.Add(mdsName.ToLower(), _audioService.GetRandomSoundClip(closeSoundEffect));
            }

            var hvrDoor = GetComponentInChildren<HVRPhysicsDoor>();
            hvrDoor.SFXOpened = _doorOpenedClips[mdsName.ToLower()];
            hvrDoor.SFXClosed = _doorClosedClips[mdsName.ToLower()];
        }
    }
}
#endif
