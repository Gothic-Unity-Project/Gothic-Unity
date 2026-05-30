using GUZ.Core.Services.Meshes;
using Reflex.Attributes;
using UnityEngine;

namespace GUZ.Core.Adapters.Meshes
{
    public class TextureAdapter : MonoBehaviour
    {
        // (Main) Menu
        public Material MenuChoiceBackMaterial;

        // Menu
        public Material ButtonMaterial;
        public Material FillerMaterial;

        // Misc
        public Material SkyMaterial;
        public Material BackgroundMaterial;


        [Inject] private readonly TextureService _textureService;


        private void Start()
        {
            _textureService.MenuChoiceBackMaterial = MenuChoiceBackMaterial;
            _textureService.ButtonMaterial = ButtonMaterial;
            _textureService.FillerMaterial = FillerMaterial;
            _textureService.SkyMaterial = SkyMaterial;
            _textureService.BackgroundMaterial = BackgroundMaterial;
        }
    }
}
