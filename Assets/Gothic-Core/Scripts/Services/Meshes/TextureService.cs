using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Services.Caches;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Meshes
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
        [Tooltip("Vanilla INV_SLOT_EQUIPPED.TGA: background of an equipped item's slot. Null if the texture is missing.")]
        public Material EquippedSlotMaterial;
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

        /// <summary>
        /// Share of Gothic's swing trail visibility (1 = Gothic). A platform sets it before Init(), e.g. in its
        /// RegisterControlModule().
        /// </summary>
        public float WeaponTrailOpacity { get; set; } = 1f;


        [Inject] private readonly TextureCacheService _textureCacheService;

        private static readonly int _cutoffId = Shader.PropertyToID("_Cutoff");


        public void Init()
        {
            MainMenuImageBackgroundMaterial = GetEmptyMaterial(MaterialExtension.BlendMode.Opaque);
            MainMenuBackgroundMaterial = GetEmptyMaterial(MaterialExtension.BlendMode.Opaque);
            MainMenuSaveLoadBackgroundMaterial = GetEmptyMaterial(MaterialExtension.BlendMode.Opaque);
            MainMenuTextImageMaterial = GetEmptyMaterial(MaterialExtension.BlendMode.Transparent);

            // Loading Bars
            GothicLoadingMenuMaterial = GetEmptyMaterial(MaterialExtension.BlendMode.Fade);
            LoadingBarBackgroundMaterial = GetEmptyMaterial(MaterialExtension.BlendMode.Fade);
            LoadingBarMaterial = GetEmptyMaterial(MaterialExtension.BlendMode.Fade);
            
            // Status Bars
            StatusBarBackgroundMaterial = GetEmptyMaterial(MaterialExtension.BlendMode.Opaque);
            StatusBarHealthMaterial = GetEmptyMaterial(MaterialExtension.BlendMode.Opaque);
            StatusBarManaMaterial = GetEmptyMaterial(MaterialExtension.BlendMode.Opaque);
            StatusBarMiscMaterial = GetEmptyMaterial(MaterialExtension.BlendMode.Opaque);

            // Menu
            ArrowUpMaterial = GetEmptyMaterial(MaterialExtension.BlendMode.Fade);
            ArrowDownMaterial = GetEmptyMaterial(MaterialExtension.BlendMode.Fade);
            ArrowLeftMaterial = GetEmptyMaterial(MaterialExtension.BlendMode.Fade);
            
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

            var equippedSlotTexture = _textureCacheService.TryGetTexture("INV_SLOT_EQUIPPED.TGA");
            if (equippedSlotTexture == null)
            {
                Logger.LogWarning("INV_SLOT_EQUIPPED.TGA not found: equipped items get no slot highlight.", LogCat.Mesh);
                EquippedSlotMaterial = null;
            }
            else
            {
                // Created only when the texture exists: a missing one would leave an unused material behind.
                EquippedSlotMaterial = GetEmptyMaterial(MaterialExtension.BlendMode.Fade);
                EquippedSlotMaterial.mainTexture = equippedSlotTexture;
            }

            // Menu
            ArrowUpMaterial.mainTexture = _textureCacheService.TryGetTexture("O.TGA");
            ArrowDownMaterial.mainTexture = _textureCacheService.TryGetTexture("U.TGA");
            ArrowLeftMaterial.mainTexture = _textureCacheService.TryGetTexture("L.TGA");
            
            // Misc
            WeaponTrailMaterial.mainTexture = _textureCacheService.TryGetTexture("ZWEAPONTRAIL.TGA");
            // Gothic's alpha 135/255 ≈ 0.53, scaled by WeaponTrailOpacity. Gothic/UI blends One/OneMinusSrcAlpha
            // without premultiplying, so the color is scaled too, and it clips at _Cutoff (0.5, which only the old 0.53
            // passed - a lower alpha alone made the trail vanish): scaled the same way, the same pixels stay.
            var trailAlpha = 135f / 255f;
            var opacity = WeaponTrailOpacity;
            WeaponTrailMaterial.color = new Color(opacity, opacity, opacity, trailAlpha * opacity);
            WeaponTrailMaterial.SetFloat(_cutoffId, WeaponTrailMaterial.GetFloat(_cutoffId) * opacity);
        }

        public void SetTexture(string texture, Material material)
        {
            material.mainTexture = _textureCacheService.TryGetTexture(texture);
        }

        public Material GetEmptyMaterial(MaterialExtension.BlendMode blendMode)
        {
            var standardShader = Constants.ShaderUI;
            var material = new Material(standardShader);

            switch (blendMode)
            {
                case MaterialExtension.BlendMode.Opaque:
                    material.ToOpaqueMode();
                    break;
                case MaterialExtension.BlendMode.Fade:
                    material.ToFadeMode();
                    break;
                case MaterialExtension.BlendMode.Transparent:
                    material.ToTransparentMode();
                    break;
            }

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
