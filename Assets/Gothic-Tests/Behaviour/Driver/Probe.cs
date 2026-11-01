using System;
using System.Collections;
using System.IO;
using Gothic.Core.Const;
using Gothic.Core.Logging;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;
using Object = UnityEngine.Object;

namespace Gothic.Tests.Behaviour.Driver
{
    /// <summary>
    /// Looks at the rendered game. Brightness is meant for comparisons at the same spot (light off vs on), not for
    /// absolute values: exposure, sky and time of day shift those.
    /// </summary>
    public static class Probe
    {
        private const int _size = 32;
        private const float _height = 1.5f; // m above the point
        private const float _fieldOfView = 40f;
        private const int _frames = 3;
        private const float _groundSearchHeight = 2f;

        /// <summary>
        /// The ground under the point (the first surface below it), or the point if there is none.
        /// </summary>
        public static Vector3 GroundBelow(Vector3 point)
        {
            return Physics.Raycast(point + Vector3.up * _groundSearchHeight, Vector3.down, out var hit,
                _groundSearchHeight * 2f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                ? hit.point
                : point;
        }

        /// <summary>
        /// Mean luminance (0-1) of what a camera 1.5 m above the point sees looking straight down, over 3 frames.
        /// The player's own body, hands and held items aren't rendered.
        /// </summary>
        /// <param name="imagePath">Where the last frame is saved as PNG, null for none.</param>
        public static IEnumerator Brightness(Vector3 point, Action<float> result, string imagePath = null)
        {
            var target = new RenderTexture(_size, _size, 24, RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(_size, _size, TextureFormat.RGBA32, false);
            var go = new GameObject("BehaviourTest brightness probe");
            try
            {
                var camera = go.AddComponent<Camera>();
                camera.stereoTargetEye = StereoTargetEyeMask.None;
                camera.targetTexture = target;
                camera.fieldOfView = _fieldOfView;
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = _height * 4f;
                camera.cullingMask = GetCullingMask();
                camera.transform.SetPositionAndRotation(point + Vector3.up * _height,
                    Quaternion.LookRotation(Vector3.down, Vector3.forward));

                var sum = 0f;
                for (var i = 0; i < _frames; i++)
                {
                    yield return new WaitForEndOfFrame();
                    sum += Read(target, pixels);
                }

                if (imagePath != null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(imagePath)!);
                    File.WriteAllBytes(imagePath, pixels.EncodeToPNG());
                }

                result(sum / _frames);
            }
            finally
            {
                Object.Destroy(go);
                target.Release();
                Object.Destroy(target);
                Object.Destroy(pixels);
            }
        }

        /// <summary>
        /// What the game view shows at the end of this frame, saved as PNG. A capture that fails is logged, not
        /// thrown - it is taken while a failure is reported.
        /// </summary>
        public static IEnumerator Screenshot(string path)
        {
            yield return new WaitForEndOfFrame();

            Texture2D shot = null;
            try
            {
                shot = ScreenCapture.CaptureScreenshotAsTexture();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, shot.EncodeToPNG());
            }
            catch (Exception e)
            {
                Logger.LogWarning($"[BehaviourTests] No screenshot {path}: {e.Message}", LogCat.Debug);
            }
            finally
            {
                if (shot != null)
                    Object.Destroy(shot);
            }
        }

        private static float Read(RenderTexture target, Texture2D pixels)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, _size, _size), 0, 0, false);
            pixels.Apply(false);
            RenderTexture.active = previous;

            var sum = 0f;
            var colors = pixels.GetPixels32();
            foreach (var color in colors)
                sum += (0.2126f * color.r + 0.7152f * color.g + 0.0722f * color.b) / 255f;
            return sum / colors.Length;
        }

        /// <summary>
        /// The main camera's layers without the player (body, hands, held and grabbed items, UI).
        /// </summary>
        private static int GetCullingMask()
        {
            var mask = Camera.main != null ? Camera.main.cullingMask : ~0;
            foreach (var layer in new int[] { Constants.PlayerLayer, Constants.HandLayer, Constants.DynamicPoseLayer,
                         Constants.GrabbableLayer, Constants.UILayer, Constants.VobItemNoWorldCollision })
            {
                if (layer >= 0)
                    mask &= ~(1 << layer);
            }

            return mask;
        }
    }
}
