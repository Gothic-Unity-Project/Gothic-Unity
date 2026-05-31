#if GUZ_HVR_INSTALLED
using Gothic.Core.Services.Meshes;
using Gothic.Core.Services.Vm;
using Gothic.Core;
using Gothic.Core.Const;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.UI;

namespace Gothic.VR.Adapters.UI.Menus
{
    public class TutorialHandler : MonoBehaviour
    {
        [SerializeField] private Image[] _backgroundImages;

        [Inject] private readonly VmService _vmService;
        [Inject] private readonly TextureService _textureService;

        
        private void Awake()
        {
            gameObject.SetActive(false);
        }

        private void Start()
        {
            var backPic = _textureService.GetMaterial(_vmService.BackPic);
            
            foreach (var image in _backgroundImages)
            {
                image.material = backPic;
            }
        }
    }
}
#endif
