using System;
using System.Reflection;
using GUZ.Core.Const;
using GUZ.Core.Logging;
using GUZ.Core.Services.Caches;
using JetBrains.Annotations;
using Reflex.Attributes;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore;
using Logger = GUZ.Core.Logging.Logger;

namespace GUZ.Core.Services.UI
{
    public class FontService
    {
        public TMP_FontAsset DefaultFont;
        public TMP_SpriteAsset DefaultSpriteAsset;
        public TMP_SpriteAsset HighlightSpriteAsset;

        [Inject] private readonly TextureCacheService _textureCacheService;
        [Inject] private readonly MultiTypeCacheService _multiTypeCacheService;
        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;

        public void Create()
        {
            DefaultSpriteAsset = TryGetFont("font_old_20_white.FNT");
            HighlightSpriteAsset = TryGetFont("font_old_20_white_hi.FNT");

            TMP_Settings.defaultSpriteAsset = DefaultSpriteAsset;
            TMP_Settings.defaultFontAsset = DefaultFont;
        }

        [CanBeNull]
        public TMP_SpriteAsset TryGetFont(string fontName)
        {
            var preparedKey = $"{_resourceCacheService.GetPreparedKey(fontName)}.fnt";
            if (_multiTypeCacheService.FontCache.TryGetValue(preparedKey, out var data))
            {
                return data;
            }

            var font = _resourceCacheService.TryGetFont(preparedKey);
            var fontTexture = _textureCacheService.TryGetTexture(preparedKey);

            if (font == null || fontTexture == null)
            {
                Logger.LogError($"[{nameof(FontService)}]: Could not find font {fontName}", LogCat.Misc);
                return null;
            }
            var spriteAsset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();

            for (var i = 0; i < font.Glyphs.Count; i++)
            {
                var x = font.Glyphs[i].topLeft.X * fontTexture.width;
                x = x < 0 ? 0 : x;
                var y = font.Glyphs[i].topLeft.Y * fontTexture.height;
                var w = font.Glyphs[i].width;
                var h = font.Height;
                var newSprite = Sprite.Create(fontTexture, new Rect(x, y, w, h),
                    new Vector2(font.Glyphs[i].topLeft.X, font.Glyphs[i].bottomRight.Y));

                var spriteGlyph = new TMP_SpriteGlyph
                {
                    glyphRect = new GlyphRect
                    {
                        width = w,
                        height = h,
                        x = (int)x,
                        y = (int)y
                    },
                    metrics = new GlyphMetrics
                    {
                        width = w,
                        height = -h,
                        horizontalBearingY = 0,
                        horizontalBearingX = 0,
                        horizontalAdvance = w
                    },
                    index = (uint)i,
                    sprite = newSprite,
                    scale = -1
                };

                
                // Convert the glyph index (treated as a codepage-byte) to its Unicode equivalent
                var unicodeChars = _gameStateService.Encoding.GetChars(new[]{(byte)i});
                var unicodeValue = (uint)unicodeChars[0];  // Return the Unicode character's code point
                var spriteCharacter = new TMP_SpriteCharacter(unicodeValue, spriteGlyph);

                spriteAsset.spriteGlyphTable.Add(spriteGlyph);
                spriteAsset.spriteCharacterTable.Add(spriteCharacter);
            }

            spriteAsset.name = preparedKey;
            spriteAsset.material = GetDefaultSpriteMaterial(fontTexture);
            spriteAsset.spriteSheet = fontTexture;

            // Get the Type of the TMP_SpriteAsset
            var spriteAssetType = spriteAsset.GetType();

            // Get the FieldInfo of the 'm_Version' field
            var versionField = spriteAssetType.GetField("m_Version", BindingFlags.NonPublic | BindingFlags.Instance);

            versionField.SetValue(spriteAsset, "1.0.0"); // setting this as to skip "UpgradeSpriteAsset"

            spriteAsset.UpdateLookupTables();

            _multiTypeCacheService.FontCache[preparedKey] = spriteAsset;

            return spriteAsset;
        }

        private Material GetDefaultSpriteMaterial(Texture2D spriteSheet = null)
        {
            ShaderUtilities.GetShaderPropertyIDs();

            // Add a new material
            var shader = Constants.ShaderTMPSprite;
            var tempMaterial = new Material(shader);
            tempMaterial.SetTexture(ShaderUtilities.ID_MainTex, spriteSheet);

            return tempMaterial;
        }
    }
}
