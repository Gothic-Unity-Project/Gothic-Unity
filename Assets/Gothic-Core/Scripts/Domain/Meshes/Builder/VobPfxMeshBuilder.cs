using System;
using GUZ.Core.Adapters.Properties.Vob;
using GUZ.Core.Const;
using GUZ.Core.Extensions;
using GUZ.Core.Logging;
using GUZ.Core.Models.Caches;
using GUZ.Core.Services.Caches;
using GUZ.Core.Services.Meshes;
using MyBox;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.Rendering;
using Logger = GUZ.Core.Logging.Logger;
using Object = System.Object;

namespace GUZ.Core.Domain.Meshes.Builder
{
    /// <summary>
    /// Please check description at worldofgothic for more details:
    /// https://www.worldofgothic.de/modifikation/index.php?go=particelfx
    /// </summary>
    public class VobPfxMeshBuilder : AbstractMeshBuilder
    {
        [Inject] private readonly TextureService _textureService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;

        private string _visualName;
        private bool _destroyAfterPlay;


        public void SetPfxData(string visualName)
        {
            _visualName = visualName;
        }
        
        public void SetDestroyAfterPlay(bool destroyAfterPlay)
        {
            _destroyAfterPlay = destroyAfterPlay;
        }
        
        public override GameObject Build()
        {
            var pfxGo = _resourceCacheService.TryGetPrefabObject(PrefabType.VobPfx, parent: RootGo)!;
            pfxGo.name = _visualName;

            var pfx = VmCacheService.TryGetPfxData(_visualName);
            var particleSystem = pfxGo.GetComponent<ParticleSystem>();

            pfxGo.GetComponent<VobPfxProperties>().PfxData = pfx;

            particleSystem.Stop();

            var gravity = pfx.FlyGravityS.Split();
            float gravityX = 0f, gravityY = 0f, gravityZ = 0f;
            if (gravity.Length == 3)
            {
                // Gravity values in Gothic are very small (e.g., -0.0003)
                // Multiplying by 1k - Blood effects for Zombies are spreading good then.
                gravityX = float.Parse(gravity[0]) * 10;
                gravityY = float.Parse(gravity[1]) * 10;
                gravityZ = float.Parse(gravity[2]) * 10;
            }

            // Main module
            {
                var mainModule = particleSystem.main;
                // I assume we need to change milliseconds to seconds.
                var minLifeTime = (pfx.LspPartAvg - pfx.LspPartVar) / 1000;
                var maxLifeTime = (pfx.LspPartAvg + pfx.LspPartVar) / 1000;
                mainModule.duration = 1f; // I assume pfx data wants a cycle being 1 second long.
                mainModule.startLifetime = new ParticleSystem.MinMaxCurve(minLifeTime, maxLifeTime);
                mainModule.loop = Convert.ToBoolean(pfx.PpsIsLooping);

                // Velocity in Gothic PFX seems already in a small unit (samples use 0.1). 
                // For blood splatter effects it's tested to work with a scale factor for spreading
                var velocityScale = 10f; // Empirical value for blood splatter
                var minSpeed = Mathf.Max(0f, (pfx.VelAvg - pfx.VelVar) * velocityScale);
                var maxSpeed = Mathf.Max(0f, (pfx.VelAvg + pfx.VelVar) * velocityScale);
                mainModule.startSpeed = new ParticleSystem.MinMaxCurve(minSpeed, maxSpeed);
                
                // Always disable Unity's built-in gravity - use Force over Lifetime instead
                mainModule.gravityModifier = 0f;

                // Simulation space from FOR switches (prefer dir FOR if present)
                var simFor = (pfx.DirForS ?? pfx.ShpForS)?.ToUpperInvariant();
                if (simFor == "WORLD")
                    mainModule.simulationSpace = ParticleSystemSimulationSpace.World;
                else
                    mainModule.simulationSpace = ParticleSystemSimulationSpace.Local;

                // Start size from visSizeStart (cm->m). Allow end scale via Size over Lifetime below.
                if (!string.IsNullOrEmpty(pfx.VisSizeStartS) && pfx.VisSizeStartS != "=")
                {
                    var sizeTokens = pfx.VisSizeStartS.Split();
                    if (sizeTokens.Length >= 1 && float.TryParse(sizeTokens[0], out var sx))
                    {
                        // Billboard uses uniform size; take X as base.
                        mainModule.startSize = Mathf.Max(0.001f, sx / 100f);
                    }
                }
            }

            // Emission module
            {
                var emissionModule = particleSystem.emission;
                
                // Gothic's ppsValue is particles/second, but we need to scale it down significantly.
                // dividing by 100 gives the right amount of blood emitter for zombies. Keeping it for now.
                var scaledEmissionRate = pfx.PpsValue / 100f;
                
                // Use ppsScaleKeys to modulate emission over normalized time [0..1]
                if (pfx.PpsScaleKeysS.NotNullOrEmpty() && pfx.PpsScaleKeysS != "=")
                {
                    var keys = pfx.PpsScaleKeysS.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (keys.Length > 0)
                    {
                        var curve = new AnimationCurve();
                        for (var i = 0; i < keys.Length; i++)
                        {
                            if (!float.TryParse(keys[i], out var k)) k = 1f;
                            var t = keys.Length == 1 ? 0f : (float)i / (keys.Length - 1);
                            curve.AddKey(new Keyframe(t, k));
                        }

                        // Optional smoothing
                        if (Convert.ToBoolean(pfx.PpsIsSmooth))
                        {
                            for (var i = 0; i < curve.keys.Length; i++)
                            {
                                curve.SmoothTangents(i, 0f);
                            }
                        }

                        // Apply with scaled multiplier
                        emissionModule.rateOverTime = new ParticleSystem.MinMaxCurve(scaledEmissionRate, curve);
                    }
                    else
                    {
                        emissionModule.rateOverTime = new ParticleSystem.MinMaxCurve(scaledEmissionRate);
                    }
                }
                else
                {
                    emissionModule.rateOverTime = new ParticleSystem.MinMaxCurve(scaledEmissionRate);
                }
            }

            // Force over Lifetime module
            {
                var forceModule = particleSystem.forceOverLifetime;
                if (gravity.Length == 3)
                {
                    forceModule.enabled = true;
                    forceModule.x = gravityX;
                    forceModule.y = gravityY;
                    forceModule.z = gravityZ;
                }
            }

            // Color over Lifetime module
            {
                var colorOverTime = particleSystem.colorOverLifetime;
                colorOverTime.enabled = true;
                var gradient = new Gradient();
                var colorStart = pfx.VisTexColorStartS.Split();
                var colorEnd = pfx.VisTexColorEndS.Split();
                gradient.SetKeys(
                    new GradientColorKey[]
                    {
                        new(
                            new Color(float.Parse(colorStart[0]) / 255, float.Parse(colorStart[1]) / 255,
                                float.Parse(colorStart[2]) / 255),
                            0f),
                        new(
                            new Color(float.Parse(colorEnd[0]) / 255, float.Parse(colorEnd[1]) / 255,
                                float.Parse(colorEnd[2]) / 255), 1f)
                    },
                    new GradientAlphaKey[]
                    {
                        new(pfx.VisAlphaStart / 255, 0),
                        new(pfx.VisAlphaEnd / 255, 1)
                    });
                colorOverTime.color = gradient;
            }

            // Size over lifetime module (from vis size)
            {
                var sizeOverTime = particleSystem.sizeOverLifetime;
                sizeOverTime.enabled = false;

                // Gothic uses visSizeStart_S as base size and visSizeEndScale as multiplier over life
                var endScale = pfx.VisSizeEndScale;
                if (endScale > 0f && Math.Abs(endScale - 1f) > 0.001f)
                {
                    var curve = new AnimationCurve();
                    curve.AddKey(0f, 1f);
                    curve.AddKey(1f, endScale);
                    sizeOverTime.enabled = true;
                    sizeOverTime.size = new ParticleSystem.MinMaxCurve(1f, curve);
                }
                else if (pfx.ShpScaleKeysS.NotNullOrEmpty() && pfx.ShpScaleKeysS != "=")
                {
                    // We do not support animating emitter shape scale yet; log once
                    Logger.LogWarning($"shpScaleKeys_S currently not applied to the emitter shape (Unity API limitation without custom script).", LogCat.Mesh);
                }
            }

            // Renderer module
            {
                var rendererModule = pfxGo.GetComponent<ParticleSystemRenderer>();
                // FIXME - Move to a cached constant value
                var standardShader = Constants.ShaderUnlitParticles;
                var material = new Material(standardShader);
                rendererModule.material = material;
                _textureService.SetTexture(pfx.VisNameS, rendererModule.material);
                // renderer.material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest; // First check with no change.

                switch (pfx.VisAlphaFuncS.ToUpper())
                {
                    case "BLEND":
                        rendererModule.material.ToTransparentMode(); // e.g. leaves.pfx.
                        break;
                    case "ADD":
                        rendererModule.material.ToAdditiveMode();
                        break;
                    default:
                        Logger.LogWarning($"Particle AlphaFunc {pfx.VisAlphaFuncS} not yet handled.", LogCat.Mesh);
                        break;
                }

                // makes the material render both faces
                rendererModule.material.SetInt("_Cull", (int)CullMode.Off);

                switch (pfx.VisOrientationS)
                {
                    case "NONE":
                        rendererModule.alignment = ParticleSystemRenderSpace.View;
                        break;
                    case "WORLD":
                        rendererModule.alignment = ParticleSystemRenderSpace.World;
                        break;
                    case "VELO":
                        rendererModule.alignment = ParticleSystemRenderSpace.Velocity;
                        break;
                    default:
                        Logger.LogWarning($"visOrientation {pfx.VisOrientationS} not yet handled.", LogCat.Mesh);
                        break;
                }

                // Billboard vs Mesh (quad poly)
                // 1 => quad billboard (default). 0 => tri mesh (not supported directly) -> keep billboard and warn.
                if (pfx.VisTexIsQuadPoly == 0)
                {
                    Logger.LogWarning("visTexIsQuadPoly=0 (triMesh) is not supported; using billboard quad.", LogCat.Mesh);
                }
            }

            // Shape module
            {
                var shapeModule = particleSystem.shape;
                switch (pfx.ShpTypeS.ToUpper())
                {
                    case "SPHERE":
                        shapeModule.shapeType = ParticleSystemShapeType.Sphere;
                        break;
                    case "CIRCLE":
                        shapeModule.shapeType = ParticleSystemShapeType.Circle;
                        break;
                    case "BOX":
                        shapeModule.shapeType = ParticleSystemShapeType.Box;
                        break;
                    case "MESH":
                        shapeModule.shapeType = ParticleSystemShapeType.Mesh;
                        break;
                    case "POINT":
                        shapeModule.shapeType = ParticleSystemShapeType.Sphere;
                        shapeModule.radius = 0f;
                        break;
                    case "LINE":
                        // Use circle with very small radius and arc to approximate a line, or leave as point with radiusThickness
                        shapeModule.shapeType = ParticleSystemShapeType.Cone;
                        shapeModule.radius = 0f;
                        shapeModule.angle = 0f;
                        break;
                    default:
                        Logger.LogWarning($"Particle ShapeType {pfx.ShpTypeS} not yet handled.", LogCat.Mesh);
                        break;
                }

                var shapeDimensions = pfx.ShpDimS.Split();
                switch (shapeDimensions.Length)
                {
                    case 1:
                        // cm in m
                        shapeModule.radius = float.Parse(shapeDimensions[0]) / 100;
                        break;
                    case 2:
                        // e.g. circle radius (x) and thickness (y) not supported separately. Use x as radius
                        if (float.TryParse(shapeDimensions[0], out var r))
                            shapeModule.radius = r / 100f;
                        break;
                    case 3:
                        // Box or sphere scale in cm -> m
                        if (float.TryParse(shapeDimensions[0], out var sx) &&
                            float.TryParse(shapeDimensions[1], out var sy) &&
                            float.TryParse(shapeDimensions[2], out var sz))
                        {
                            shapeModule.scale = new Vector3(sx / 100f, sy / 100f, sz / 100f);
                        }
                        break;
                    default:
                        Logger.LogWarning($"shpDim >{pfx.ShpDimS}< not yet handled", LogCat.Mesh);
                        break;
                }

                // Apply direction mode and angle variations
                if (pfx.DirModeS.EqualsIgnoreCase("RAND"))
                {
                    // For random direction with angle spread, use Sphere shape to emit in all directions
                    // The velocity variations will be handled by start speed and velocity over lifetime
                    shapeModule.shapeType = ParticleSystemShapeType.Sphere;
                    shapeModule.radius = 0.01f; // Very small radius for point-like emission
                    shapeModule.radiusThickness = 0f; // Emit from surface only
                    
                    // Enable randomized direction
                    shapeModule.randomDirectionAmount = 1f;
                }
                else if (pfx.DirModeS.EqualsIgnoreCase("DIR"))
                {
                    // Fixed direction using head/elev angles
                    shapeModule.angle = 0f;
                    shapeModule.randomDirectionAmount = 0f;
                    shapeModule.rotation = new Vector3(pfx.DirAngleElev, pfx.DirAngleHead, 0);
                }

                var shapeOffsetVec = pfx.ShpOffsetVecS.Split();
                if (float.TryParse(shapeOffsetVec[0], out var x) && float.TryParse(shapeOffsetVec[1], out var y) &&
                    float.TryParse(shapeOffsetVec[2], out var z))
                {
                    shapeModule.position = new Vector3(x / 100, y / 100, z / 100);
                }

                shapeModule.alignToDirection = false; // Don't align to direction for blood splatter
            }

            // Velocity over Lifetime module (for directional spread and TARGET)
            {
                var velocityModule = particleSystem.velocityOverLifetime;
                
                if (pfx.DirModeS.ToUpper() == "RAND")
                {
                    // For RAND mode, particles should spread in multiple directions
                    // The shape module handles the initial random direction
                    // We don't need additional velocity over lifetime for basic spreading
                    velocityModule.enabled = false;
                }
                else if (pfx.DirModeS.ToUpper() == "TARGET" && !string.IsNullOrEmpty(pfx.DirModeTargetPosS) && pfx.DirModeTargetPosS != "=")
                {
                    // Direct particles towards a fixed target position; we override main.startSpeed
                    var posTokens = pfx.DirModeTargetPosS.Split();
                    if (posTokens.Length >= 3 &&
                        float.TryParse(posTokens[0], out var tx) &&
                        float.TryParse(posTokens[1], out var ty) &&
                        float.TryParse(posTokens[2], out var tz))
                    {
                        var target = new Vector3(tx / 100f, ty / 100f, tz / 100f);
                        var origin = Vector3.zero; // assuming emitter origin in Local space by default
                        var dir = (target - origin).normalized;

                        // Use average speed
                        var speed = Mathf.Max(0f, pfx.VelAvg);
                        velocityModule.enabled = true;
                        velocityModule.space = ParticleSystemSimulationSpace.Local;
                        velocityModule.x = new ParticleSystem.MinMaxCurve(dir.x * speed);
                        velocityModule.y = new ParticleSystem.MinMaxCurve(dir.y * speed);
                        velocityModule.z = new ParticleSystem.MinMaxCurve(dir.z * speed);

                        // Ensure start speed does not add extra magnitude
                        var mainModule = particleSystem.main;
                        mainModule.startSpeed = 0f;
                    }
                }
            }

            // Collision module
            {
                var collision = particleSystem.collision;
                // ZenKit naming uses FlyCollisionDetectionB
                if (pfx.FlyCollisionDetectionB > 0)
                {
                    collision.enabled = true;
                    collision.type = ParticleSystemCollisionType.World;
                    collision.mode = ParticleSystemCollisionMode.Collision3D;
                    collision.collidesWith = ~0; // everything
                    collision.bounce = 0f;
                    collision.lifetimeLoss = 0f;
                }
            }

            // Trails module (basic)
            {
                var trails = particleSystem.trails;
                var wantTrail = pfx.TrlWidth > 0f || (!string.IsNullOrEmpty(pfx.TrlTextureS) && pfx.TrlTextureS != "=");
                if (wantTrail)
                {
                    trails.enabled = true;
                    trails.ratio = 1f;
                    trails.lifetime = 0.25f;
                    trails.widthOverTrail = pfx.TrlWidth > 0 ? new ParticleSystem.MinMaxCurve(pfx.TrlWidth / 100f) : new ParticleSystem.MinMaxCurve(0.1f);

                    // Assign trail material if texture provided
                    if (!string.IsNullOrEmpty(pfx.TrlTextureS) && pfx.TrlTextureS != "=")
                    {
                        var trailMat = new Material(Constants.ShaderUnlitParticles);
                        _textureService.SetTexture(pfx.TrlTextureS, trailMat);
                        var renderer = pfxGo.GetComponent<ParticleSystemRenderer>();
                        renderer.trailMaterial = trailMat;
                    }
                }
            }

            particleSystem.Play();

            // WARNING: If we provided an existing GO, then it will also destroy other Components. We assume it's a new GO() for Destroy only.
            if (!particleSystem.main.loop && _destroyAfterPlay)
            {
                UnityEngine.Object.Destroy(RootGo, particleSystem.main.duration);
            }

            return pfxGo;
        }
    }
}
