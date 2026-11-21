using System.Collections.Generic;
using Gothic.Core.Services.Vobs;
using Reflex.Attributes;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Gothic.Core.Domain.Meshes
{
    /// <summary>
    /// Draws one item into a small picture: the item's own mesh (VobService.CreateItemMesh, as in a hand or the
    /// favorites ring), far below every world, lit by its own light and seen by a hidden orthographic camera.
    /// Everything it builds is gone when Render returns. Owned by ItemIconService.
    /// </summary>
    public class ItemIconRenderDomain
    {
        private const int _size = 128;
        private const float _padding = 1.05f;
        // Far below every world, so nothing else is in the camera's box or the light's range.
        private static readonly Vector3 _stagePosition = new(0f, -10000f, 0f);
        // Seen from the front and a bit above: flat items (runes, scrolls, tablets) lie in the x/z plane.
        private static readonly Quaternion _itemRotation = Quaternion.Euler(-60f, 30f, 0f);

        [Inject] private readonly VobService _vobService;

        private static readonly List<Vector3> _vertexBuffer = new();

        /// <summary>
        /// Null: the item has no mesh or no material to draw.
        /// </summary>
        public Texture2D Render(string itemInstance)
        {
            var stage = new GameObject($"_ItemIcon {itemInstance}");
            stage.transform.position = _stagePosition;
            RenderTexture target = null;
            var previous = RenderTexture.active;
            try
            {
                var holder = new GameObject("Item");
                holder.transform.SetParent(stage.transform, false);
                holder.transform.localRotation = _itemRotation;
                _vobService.CreateItemMesh(itemInstance, holder);

                if (!TryGetBounds(holder, out var bounds, out var layers))
                    return null;

                // The shader adds per-object lights to the sun and ambient: this one makes every icon look alike,
                // whatever the time of day.
                var light = new GameObject("Light").AddComponent<Light>();
                light.transform.SetParent(stage.transform, false);
                light.transform.position = bounds.center + new Vector3(-1f, 1f, -2f) * bounds.extents.magnitude;
                light.type = LightType.Point;
                light.range = bounds.extents.magnitude * 6f;
                light.intensity = 2f;
                light.cullingMask = layers;

                var camera = new GameObject("Camera").AddComponent<Camera>();
                camera.transform.SetParent(stage.transform, false);
                camera.transform.SetPositionAndRotation(bounds.center - Vector3.forward * (bounds.extents.z + 1f),
                    Quaternion.identity);
                camera.enabled = false;
                camera.orthographic = true;
                camera.orthographicSize = Mathf.Max(bounds.extents.x, bounds.extents.y, 0.01f) * _padding;
                camera.nearClipPlane = 0.01f;
                camera.farClipPlane = bounds.size.z + 2f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.clear;
                camera.cullingMask = layers;
                camera.stereoTargetEye = StereoTargetEyeMask.None;

                target = RenderTexture.GetTemporary(_size, _size, 24, RenderTextureFormat.ARGB32);
                camera.targetTexture = target;
                camera.Render();
                camera.targetTexture = null;

                RenderTexture.active = target;
                var icon = new Texture2D(_size, _size, TextureFormat.RGBA32, false)
                {
                    name = $"ItemIcon {itemInstance}",
                    wrapMode = TextureWrapMode.Clamp
                };
                icon.ReadPixels(new Rect(0, 0, _size, _size), 0, 0);
                icon.Apply(false, true);
                return icon;
            }
            finally
            {
                RenderTexture.active = previous;
                if (target != null)
                    RenderTexture.ReleaseTemporary(target);
                // At once: the next icon uses the same spot.
                Object.DestroyImmediate(stage);
            }
        }

        /// <summary>
        /// The visible part only: renderers without a material (a texture the installation lacks) draw nothing.
        /// From the vertices, not Renderer.bounds - the tilted item's axis-aligned box would leave it small.
        /// </summary>
        private static bool TryGetBounds(GameObject holder, out Bounds bounds, out int layers)
        {
            bounds = default;
            layers = 0;
            var hasBounds = false;
            foreach (var renderer in holder.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.sharedMaterial == null)
                    continue;

                var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null || !mesh.isReadable)
                {
                    Encapsulate(ref bounds, ref hasBounds, renderer.bounds);
                }
                else
                {
                    var toWorld = renderer.transform.localToWorldMatrix;
                    _vertexBuffer.Clear();
                    mesh.GetVertices(_vertexBuffer);
                    foreach (var vertex in _vertexBuffer)
                    {
                        var point = new Bounds(toWorld.MultiplyPoint3x4(vertex), Vector3.zero);
                        Encapsulate(ref bounds, ref hasBounds, point);
                    }
                }
                layers |= 1 << renderer.gameObject.layer;
            }

            return hasBounds;
        }

        private static void Encapsulate(ref Bounds bounds, ref bool hasBounds, Bounds part)
        {
            if (hasBounds)
                bounds.Encapsulate(part);
            else
                bounds = part;
            hasBounds = true;
        }
    }
}
