using System.Collections.Generic;
using GUZ.Core.Services.Meshes;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.UI;

namespace GUZ.Core.Adapters.UI.LoadingBars
{
    public abstract class AbstractLoadingBarHandler : MonoBehaviour
    {
        public Image LoadingImage;
        public Image ProgressBackgroundImage;
        public Image ProgressBarImage;

        
        [Inject] protected readonly TextureService TextureService;


        public abstract List<string> GetProgressTypes();
        
        protected virtual void Start()
        {
            SetMaterials();
        }
        
        protected virtual void SetMaterials()
        {
            LoadingImage.material = TextureService.GothicLoadingMenuMaterial;
            ProgressBackgroundImage.material = TextureService.LoadingBarBackgroundMaterial;
            ProgressBarImage.material = TextureService.LoadingBarMaterial;
        }
    }
}
