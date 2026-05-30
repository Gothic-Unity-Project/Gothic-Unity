using GUZ.Core.Const;
using GUZ.Core.Extensions;
using GUZ.Core.Services.Caches;
using Reflex.Attributes;
using UnityEngine;

namespace GUZ.Core.Services.Meshes
{
    public class TextureService
    {
        // (Main) Menu
        public Material MainMenuImageBackgroundMaterial;
        public Material MainMenuBackgroundMaterial;
        public Material MainMenuSaveLoadBackgroundMaterial;
        public Material MainMenuTextImageMaterial;
        public Material MenuChoiceBackMaterial;

        // Menu
        public Material GothicLoadingMenuMaterial;
        public Material ButtonMaterial;
        public Material FillerMaterial;
        public Material ArrowUpMaterial;
        public Material ArrowDownMaterial;
        public Material ArrowLeftMaterial;

        // Loading Bars
        public Material LoadingBarBackgroundMaterial;
        public Material LoadingBarMaterial;

        // Status Bars
        public Material StatusBarBackgroundMaterial;
        public Material StatusBarHealthMaterial;
        public Material StatusBarManaMaterial;
        public Material StatusBarMiscMaterial; // Air in water

        // Misc
        public Material SkyMaterial;
        public Material BackgroundMaterial;
        public Material WeaponTrailMaterial;


        [Inject] private readonly TextureCacheService _textureCacheService;


        public void Init()
        {
            MainMenuImageBackgroundMaterial = GetEmptyMaterial(MaterialExtension.BlendMode.Opaque);
            MainMenuBackgroundMaterial = GetEmptyMaterial(MaterialExtension.BlendMode.Opaque);
            MainMenuSaveLoadBackgroundMaterial = GetEmptyMaterial(MaterialExtension.BlendMode.Opaque);
            MainMenuTextImageMaterial = GetEmptyMaterial(MaterialExtension.BlendMode.Transparent);

            // Loading Bars
            GothicLoadingMenuMaterial = GetEmptyMaterial(MaterialExtension.BlendMode.Opaque);
            LoadingBarBackgroundMaterial = GetEmptyMaterial(MaterialExtension.BlendMode.Opaque);
            LoadingBarMaterial = GetEmptyMaterial(MaterialExtension.BlendMode.Opaque);
            
            // Status Bars
            StatusBarBackgroundMaterial = GetEmptyMaterial(MaterialExtension.BlendMode.Opaque);
            StatusBarHealthMaterial = GetEmptyMaterial(MaterialExtension.BlendMode.Opaque);
            StatusBarManaMaterial = GetEmptyMaterial(MaterialExtension.BlendMode.Opaque);
            StatusBarMiscMaterial = GetEmptyMaterial(MaterialExtension.BlendMode.Opaque);

            // Menu
            ArrowUpMaterial = GetEmptyMaterial(MaterialExtension.BlendMode.Opaque);
            ArrowDownMaterial = GetEmptyMaterial(MaterialExtension.BlendMode.Opaque);
            ArrowLeftMaterial = GetEmptyMaterial(MaterialExtension.BlendMode.Opaque);
            
            // Misc
            WeaponTrailMaterial = GetEmptyMaterial(MaterialExtension.BlendMode.Transparent);
        
            
            MainMenuImageBackgroundMaterial.mainTexture = _textureCacheService.TryGetTexture("STARTSCREEN.TGA");
            MainMenuBackgroundMaterial.mainTexture = _textureCacheService.TryGetTexture("MENU_INGAME.TGA");
            MainMenuSaveLoadBackgroundMaterial.mainTexture = _textureCacheService.TryGetTexture("MENU_SAVELOAD_BACK.TGA");
            MainMenuTextImageMaterial.mainTexture = _textureCacheService.TryGetTexture("MENU_GOTHIC.TGA");
            MenuChoiceBackMaterial.mainTexture = _textureCacheService.TryGetTexture("MENU_CHOICE_BACK.TGA");

            // Loading Bars
            GothicLoadingMenuMaterial.mainTexture = _textureCacheService.TryGetTexture("LOADING.TGA");
            LoadingBarBackgroundMaterial.mainTexture = _textureCacheService.TryGetTexture("PROGRESS.TGA");
            LoadingBarMaterial.mainTexture = _textureCacheService.TryGetTexture("PROGRESS_BAR.TGA");
            
            // Status Bars
            StatusBarBackgroundMaterial.mainTexture = _textureCacheService.TryGetTexture("BAR_BACK.TGA");
            StatusBarHealthMaterial.mainTexture = _textureCacheService.TryGetTexture("BAR_HEALTH.TGA");
            StatusBarManaMaterial.mainTexture = _textureCacheService.TryGetTexture("BAR_MANA.TGA");
            StatusBarMiscMaterial.mainTexture = _textureCacheService.TryGetTexture("BAR_MISC.TGA");
            
            BackgroundMaterial.mainTexture = _textureCacheService.TryGetTexture("LOG_PAPER.TGA");
            ButtonMaterial.mainTexture = _textureCacheService.TryGetTexture("INV_SLOT.TGA");
            FillerMaterial.mainTexture = _textureCacheService.TryGetTexture("MENU_BUTTONBACK.TGA");

            // Menu
            ArrowUpMaterial.mainTexture = _textureCacheService.TryGetTexture("O.TGA");
            ArrowDownMaterial.mainTexture = _textureCacheService.TryGetTexture("U.TGA");
            ArrowLeftMaterial.mainTexture = _textureCacheService.TryGetTexture("L.TGA");
            
            // Misc
            WeaponTrailMaterial.mainTexture = _textureCacheService.TryGetTexture("ZWEAPONTRAIL.TGA");
            // Set alpha to 135/255 ≈ 0.53
            var color = WeaponTrailMaterial.color;
            color.a = 135f / 255f;
            WeaponTrailMaterial.color = color;
        }

        public void SetTexture(string texture, Material material)
        {
            material.mainTexture = _textureCacheService.TryGetTexture(texture);
        }

        public Material GetEmptyMaterial(MaterialExtension.BlendMode blendMode)
        {
            var standardShader = Constants.ShaderUnlit;
            var material = new Material(standardShader);

            switch (blendMode)
            {
                case MaterialExtension.BlendMode.Opaque:
                    material.ToOpaqueMode();
                    break;
                case MaterialExtension.BlendMode.Transparent:
                    material.ToTransparentMode();
                    break;
            }

            // Enable clipping of alpha values.
            material.EnableKeyword("_ALPHATEST_ON");

            return material;
        }

        /// <summary>
        /// Create a new material and assign texture to it.
        /// </summary>
        public Material GetMaterial(string textureName, MaterialExtension.BlendMode blendMode = MaterialExtension.BlendMode.Opaque)
        {
            var material = GetEmptyMaterial(blendMode);
            material.mainTexture = _textureCacheService.TryGetTexture(textureName);

            return material;
        }
    }
}
