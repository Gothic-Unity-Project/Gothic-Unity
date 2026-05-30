using GUZ.Core.Extensions;
using GUZ.Core.Const;
using GUZ.Core.Services.Caches;
using UnityEngine;

namespace GUZ.Core.Domain.Meshes.Builder
{
    public class PolyStripMeshBuilder : AbstractMeshBuilder
    {
        private int _numberOfSegments;
        private Vector3 _startPoint;
        private Vector3 _endPoint;

        public void SetPolyStripData(int numberSegments, Vector3 start, Vector3 end)
        {
            _numberOfSegments = numberSegments;
            _startPoint = start;
            _endPoint = end;
        }

        public override GameObject Build()
        {
            var material = new Material(Constants.ShaderThunder);

            material.ToAdditiveMode();

            var texture = TextureCacheService.TryGetTexture("THUNDER_A0.TGA");

            if (texture != null)
            {
                material.mainTexture = texture;
            }

            var direction = _endPoint - _startPoint;
            var segmentLength = direction.magnitude / _numberOfSegments;
            direction.Normalize();

            var mesh = new Mesh();
            RootGo.GetComponent<MeshFilter>().mesh = mesh;
            RootGo.GetComponent<MeshRenderer>().material = material; // Set the material

            var vertices = new Vector3[(_numberOfSegments + 1) * 2];
            var triangles = new int[_numberOfSegments * 6];
            var uv = new Vector2[(_numberOfSegments + 1) * 2];


            for (var i = 0; i <= _numberOfSegments; i++)
            {
                var segmentStart = _startPoint + direction * (segmentLength * i);

                vertices[i * 2] = segmentStart;
                vertices[i * 2 + 1] = segmentStart + new Vector3(0, 30, 0);


                uv[i * 2] = new Vector2(0, (float)i / _numberOfSegments);
                uv[i * 2 + 1] = new Vector2(1, (float)i / _numberOfSegments);

                uv[i * 2].y = 1 - uv[i * 2].y;
                uv[i * 2 + 1].y = 1 - uv[i * 2 + 1].y;

                if (i < _numberOfSegments)
                {
                    var baseIndex = i * 6;
                    triangles[baseIndex] = i * 2;
                    triangles[baseIndex + 1] = i * 2 + 1;
                    triangles[baseIndex + 2] = i * 2 + 2;

                    triangles[baseIndex + 3] = i * 2 + 2;
                    triangles[baseIndex + 4] = i * 2 + 1;
                    triangles[baseIndex + 5] = i * 2 + 3;
                }
            }

            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.uv = uv;

            return RootGo;
        }
    }
}
