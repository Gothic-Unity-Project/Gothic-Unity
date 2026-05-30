using GUZ.Core.Const;
using GUZ.Core.Services.Caches;
using MyBox;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using ZenKit.Vobs;

namespace GUZ.Core.Domain.Meshes.Builder
{
    public class VobDecalMeshBuilder : AbstractMeshBuilder
    {
        // Decals work only on URP shaders. We therefore temporarily change everything to this
        // until we know how to change specifics to the cutout only. (e.g. bushes)
        private const float _decalOpacity = 0.75f;

        private IVirtualObject _vob;
        private VisualDecal _decal;

        public void SetDecalData(IVirtualObject vob, VisualDecal decal)
        {
            _vob = vob;
            _decal = decal;
        }

        public override GameObject Build()
        {
            var decalName = _vob.Visual?.Name ?? _vob.Name;

            if (decalName.IsNullOrEmpty())
            {
                return null;
            }

            var decalProj = RootGo.AddComponent<DecalProjector>();
            var texture = TextureCacheService.TryGetTexture(decalName);

            // x/y needs to be made twice the size and transformed from cm in m.
            // z - value is close to what we see in Gothic spacer.
            decalProj.size = new Vector3(_decal.Dimension.X * 2 / 100, _decal.Dimension.Y * 2 / 100, 0.5f);
            SetPosAndRot(RootGo, RootPosition, RootRotation);

            decalProj.pivot = Vector3.zero;
            decalProj.fadeFactor = _decalOpacity;

            // FIXME use Prefab!
            // https://docs.unity3d.com/Packages/com.unity.render-pipelines.high-definition@12.0/manual/creating-a-decal-projector-at-runtime.html
            var standardShader = Constants.ShaderDecal;
            var material = new Material(standardShader);
            material.SetTexture(Shader.PropertyToID("Base_Map"), texture);

            decalProj.material = material;

            return RootGo;
        }
    }
}
