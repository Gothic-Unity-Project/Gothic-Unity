using Gothic.Core.Logging;
using Gothic.Core.Services.Meshes;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Adapters.Vob.Item
{
    public class WeaponAdapter : MonoBehaviour
    {
        [SerializeField] private TrailRenderer _trailRenderer;

        [Inject] private TextureService _textureService;
        [Inject] private Gothic.Core.Services.Config.ConfigService _configService;

        
        private void Awake()
        {
            _trailRenderer.enabled = false;
        }
        
        private void Start()
        {
            _trailRenderer.material = _textureService.WeaponTrailMaterial;
            SetTrailWidth();
        }

        public void StartTrail()
        {
            // DeveloperConfig.EnableTrailFromWeaponLength: the scale at Start was the item's in a backpack slot or
            // before it was held (XRI) - a short, oddly scaled trail in the middle of the blade. Measured now.
            if (_configService != null && _configService.Dev.EnableTrailFromWeaponLength)
                SetTrailWidthFromLength();
            _trailRenderer.enabled = true;
        }

        /// <summary>
        /// The trail as wide as the weapon is long: the mesh's longest side in world units, at its middle.
        /// </summary>
        private void SetTrailWidthFromLength()
        {
            var meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
                return;

            var bounds = meshFilter.sharedMesh.bounds;
            var scale = transform.lossyScale;
            var size = Vector3.Scale(bounds.size, new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y),
                Mathf.Abs(scale.z)));
            var length = Mathf.Max(size.x, size.y, size.z);
            _trailRenderer.startWidth = length;
            _trailRenderer.endWidth = length;
            _trailRenderer.transform.position = transform.TransformPoint(bounds.center);
        }

        public void EndTrail()
        {
            _trailRenderer.enabled = false;
        }
        
        /// <summary>
        /// Apply trail width based on Bounds of Weapon mesh.
        /// </summary>
        private void SetTrailWidth()
        {
            var meshFilter = GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                var bounds = meshFilter.sharedMesh.bounds;
                var weaponHeight = bounds.size.x;
                
                weaponHeight *= transform.parent.localScale.x;
                
                _trailRenderer.startWidth = weaponHeight;
                _trailRenderer.endWidth = weaponHeight;
                
                // Align the TrailRenderer position to the mesh bounds center
                _trailRenderer.transform.localPosition = bounds.center;
            }
            else
            {
                Logger.LogWarning("No MeshFilter found on weapon parent.", LogCat.Fight);
            }
        }
    }
}
