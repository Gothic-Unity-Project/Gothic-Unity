using System;
using Gothic.Core.Logging;
using Gothic.Core.Services.Context;
using Reflex.Attributes;
using UnityEngine;
using ZenKit;
using Logger = Gothic.Core.Logging.Logger;
using Object = UnityEngine.Object;
using Texture = ZenKit.Texture;
using TextureFormat = ZenKit.TextureFormat;

namespace Gothic.Core.Domain.SaveGames
{
    public class SaveGameThumbnailDomain
    {
        private const int _pixelsPerAxis = 256; // Default size of a G1 Thumbnail

        [Inject] private readonly ContextScreenshotService _contextScreenshotService;


        /// <summary>
        /// Needs to be called after WaitForEndOfFrame, otherwise the screenshot content is undefined.
        /// Never returns null: An empty THUMB.SAV makes ZenKit's SaveGame.Load() fail (SAVEDAT.SAV isn't read then).
        /// </summary>
        public Texture Create()
        {
            Texture2D screenshot = null;
            Texture2D formattedScreenshot = null;
            RenderTexture rt = null;
            try
            {
                screenshot = _contextScreenshotService.CaptureScreenshot();

                // Alter dimensions of screenshots to align with Gothic thumbnail format.
                rt = RenderTexture.GetTemporary(_pixelsPerAxis, _pixelsPerAxis);
                rt.filterMode = FilterMode.Bilinear;

                RenderTexture.active = rt;
                Graphics.Blit(screenshot, rt);

                formattedScreenshot = new Texture2D(_pixelsPerAxis, _pixelsPerAxis, UnityEngine.TextureFormat.RGB565,
                    false);
                formattedScreenshot.ReadPixels(new Rect(0, 0, _pixelsPerAxis, _pixelsPerAxis), 0, 0);
                formattedScreenshot.Apply();

                // Gothic textures need to be flipped to be shown correctly.
                var originalPixels = formattedScreenshot.GetPixels();
                var yFlippedPixels = new Color[originalPixels.Length];
                for (var row = 0; row < _pixelsPerAxis; ++row)
                {
                    // We iterate through every row and reverse the whole row (aka flipping y-axis)
                    Array.Copy(originalPixels, row * _pixelsPerAxis, yFlippedPixels,
                        (_pixelsPerAxis - row - 1) * _pixelsPerAxis, _pixelsPerAxis);
                }
                formattedScreenshot.SetPixels(yFlippedPixels);
                formattedScreenshot.Apply();

                return Build(formattedScreenshot.GetRawTextureData());
            }
            catch (Exception e)
            {
                Logger.LogWarning($"[SaveGame] Thumbnail couldn't be created, using a black one: {e.Message}",
                    LogCat.Loading);
                return Build(new byte[_pixelsPerAxis * _pixelsPerAxis * 2]);
            }
            finally
            {
                if (rt != null)
                {
                    RenderTexture.active = null;
                    RenderTexture.ReleaseTemporary(rt);
                }
                if (screenshot != null)
                    Object.Destroy(screenshot);
                if (formattedScreenshot != null)
                    Object.Destroy(formattedScreenshot);
            }
        }

        private static Texture Build(byte[] r5g6b5Data)
        {
            var builder = new TextureBuilder(_pixelsPerAxis, _pixelsPerAxis);
            builder.AddMipmap(r5g6b5Data, TextureFormat.R5G6B5);

            return builder.Build(TextureFormat.R5G6B5);
        }
    }
}
