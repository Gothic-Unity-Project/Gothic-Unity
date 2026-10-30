using System;
using Gothic.Core.Adapters.Properties.Vob;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Caches;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Meshes;
using JetBrains.Annotations;
using MyBox;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.Rendering;
using Logger = Gothic.Core.Logging.Logger;
using Object = System.Object;

namespace Gothic.Core.Domain.Meshes.Builder
{
    /// <summary>
    /// Please check description at worldofgothic for more details:
    /// https://www.worldofgothic.de/modifikation/index.php?go=particelfx
    /// </summary>
    public class VobPfxMeshBuilder : AbstractMeshBuilder
    {
        // Gothic's units: velocity cm/ms -> m/s, gravity cm/ms^2 -> m/s^2.
        private const float _velocityToMetersPerSecond = 10f;
        private const float _gravityToMetersPerSecond2 = 10000f;
        // Emission rate cap (particles/s) for Quest 2 - Gothic's loops stay far below it (torch 80, fire 25).
        private const float _maxEmissionRate = 500f;
        private const int _maxParticles = 1000;

        // One material per texture and blend mode, shared by all emitters (237 torches in G1's world).
        private static readonly System.Collections.Generic.Dictionary<string, Material> _materials = new();

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
            if (pfx == null)
            {
                Logger.LogWarning($"PFX '{_visualName}' not found in VM - skipping particle build.", LogCat.Mesh);
                UnityEngine.Object.Destroy(pfxGo);
                return null;
            }

            var particleSystem = pfxGo.GetComponent<ParticleSystem>();

            pfxGo.GetComponent<VobPfxProperties>().PfxData = pfx;

            particleSystem.Stop();

            var gravity = pfx.FlyGravityS.Split();
            float gravityX = 0f, gravityY = 0f, gravityZ = 0f;
            if (gravity.Length == 3)
            {
                // cm/ms^2 like the engine (OpenGothic: dir += flyGravity * dt in ms).
                gravityX = float.Parse(gravity[0]) * _gravityToMetersPerSecond2;
                gravityY = float.Parse(gravity[1]) * _gravityToMetersPerSecond2;
                gravityZ = float.Parse(gravity[2]) * _gravityToMetersPerSecond2;
            }

            // Main module
            {
                var mainModule = particleSystem.main;
                var minLifeTime = (pfx.LspPartAvg - pfx.LspPartVar) / 1000;
                var maxLifeTime = (pfx.LspPartAvg + pfx.LspPartVar) / 1000;
                // Like the engine, ppsScaleKeys are played at ppsFPS keys per second - one cycle is keys/ppsFPS long.
                mainModule.duration = GetEmissionCycleSeconds(pfx);
                mainModule.startLifetime = new ParticleSystem.MinMaxCurve(minLifeTime, maxLifeTime);
                mainModule.loop = Convert.ToBoolean(pfx.PpsIsLooping);
                // As many as the engine keeps alive at once (OpenGothic: maxLife * maxPps).
                mainModule.maxParticles = Mathf.Clamp(
                    Mathf.CeilToInt(maxLifeTime * Mathf.Min(pfx.PpsValue, _maxEmissionRate) * 1.2f) + 1, 1,
                    _maxParticles);

                var minSpeed = Mathf.Max(0f, (pfx.VelAvg - pfx.VelVar) * _velocityToMetersPerSecond);
                var maxSpeed = Mathf.Max(0f, (pfx.VelAvg + pfx.VelVar) * _velocityToMetersPerSecond);
                mainModule.startSpeed = new ParticleSystem.MinMaxCurve(minSpeed, maxSpeed);
                // Far away effects pause instead of simulating unseen.
                mainModule.cullingMode = ParticleSystemCullingMode.Pause;
                
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
                
                // Gothic's ppsValue is particles per second (OpenGothic: ppsValue * ppsScale(t)).
                var scaledEmissionRate = Mathf.Min(pfx.PpsValue, _maxEmissionRate);
                
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
                var material = GetMaterial(pfx.VisNameS, pfx.VisAlphaFuncS);
                if (material == null)
                {
                    // Like the engine, an effect without its texture isn't emitted (white squares otherwise).
                    UnityEngine.Object.Destroy(RootGo);
                    return null;
                }
                rendererModule.sharedMaterial = material;

                switch (pfx.VisOrientationS)
                {
                    case "NONE":
                        rendererModule.alignment = ParticleSystemRenderSpace.View;
                        break;
                    case "WORLD":
                        rendererModule.alignment = ParticleSystemRenderSpace.World;
                        break;
                    case "VELO":
                        // Like the engine, the quad faces the camera and stretches along its flight (lying flat along
                        // the velocity it was invisible from the side). Too slow particles stay plain billboards.
                        if ((pfx.VelAvg - pfx.VelVar) * _velocityToMetersPerSecond >= 0.1f)
                        {
                            rendererModule.renderMode = ParticleSystemRenderMode.Stretch;
                            rendererModule.velocityScale = 0f;
                            rendererModule.lengthScale = 1f;
                        }
                        else
                        {
                            rendererModule.alignment = ParticleSystemRenderSpace.View;
                        }
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
                        // Gothic's circle lies flat (OpenGothic: pos = (sin a, 0, cos a)), Unity's stands upright.
                        shapeModule.shapeType = ParticleSystemShapeType.Circle;
                        shapeModule.rotation = new Vector3(90f, 0f, 0f);
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
                        // Box half extents in cm (OpenGothic: +-dim) -> full size in m
                        if (float.TryParse(shapeDimensions[0], out var sx) &&
                            float.TryParse(shapeDimensions[1], out var sy) &&
                            float.TryParse(shapeDimensions[2], out var sz))
                        {
                            shapeModule.scale = new Vector3(sx / 50f, sy / 50f, sz / 50f);
                        }
                        break;
                    default:
                        Logger.LogWarning($"shpDim >{pfx.ShpDimS}< not yet handled", LogCat.Mesh);
                        break;
                }

                // Emitted on the shape's surface, or inside it for shpIsVolume.
                shapeModule.radiusThickness = pfx.ShpIsVolume != 0 ? 1f : 0f;

                // The flight direction is Gothic's own (OpenGothic PfxBucket), not the shape's normal.
                if (pfx.DirModeS.EqualsIgnoreCase("RAND"))
                {
                    shapeModule.randomDirectionAmount = 1f;
                }
                else if (pfx.DirModeS.EqualsIgnoreCase("DIR"))
                {
                    ApplyDirection(particleSystem, pfx);
                }
                else if (pfx.DirModeS.EqualsIgnoreCase("NONE"))
                {
                    var mainModule = particleSystem.main;
                    mainModule.startSpeed = 0f;
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

            // Particle collision with the world is too expensive on Quest 2 - left out (flyCollDet).

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

            // Placed relative to the parent like every VOB mesh (fire trees: relative to the fire's slot). Before, the
            // position was ignored and every effect burned at its parent's origin.
            SetPosAndRot(RootGo, RootPosition, RootRotation);

            particleSystem.Play();

            // WARNING: If we provided an existing GO, then it will also destroy other Components. We assume it's a new GO() for Destroy only.
            if (!particleSystem.main.loop && _destroyAfterPlay)
            {
                UnityEngine.Object.Destroy(RootGo, particleSystem.main.duration);
            }

            return pfxGo;
        }

        /// <summary>
        /// Like the engine, a cycle of ppsScaleKeys lasts keys / ppsFPS seconds; without keys 1 second.
        /// </summary>
        private static float GetEmissionCycleSeconds(ZenKit.Daedalus.ParticleEffectInstance pfx)
        {
            if (pfx.PpsScaleKeysS.IsNullOrEmpty() || pfx.PpsScaleKeysS == "=" || pfx.PpsFps <= 0f)
                return 1f;

            var keyCount = pfx.PpsScaleKeysS.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
            return keyCount > 1 ? Mathf.Max(0.1f, keyCount / pfx.PpsFps) : 1f;
        }

        /// <summary>
        /// DIR: like the engine (OpenGothic PfxBucket), every particle flies in the direction of head (around the up
        /// axis, + 90 degrees) and elev (up from the horizon), each +- its variance, at velAvg +- velVar. Unity can't vary
        /// the start direction by angles, so the flight is a velocity picked per particle between the extremes.
        /// </summary>
        private static void ApplyDirection(ParticleSystem particleSystem, ZenKit.Daedalus.ParticleEffectInstance pfx)
        {
            // HACK of the engine too (STARGATE_PARTICLES): a variance of 180 or more means none.
            var headVar = pfx.DirAngleHeadVar >= 180f ? 0f : pfx.DirAngleHeadVar;
            var elevVar = pfx.DirAngleElevVar >= 180f ? 0f : pfx.DirAngleElevVar;
            var minSpeed = Mathf.Max(0f, (pfx.VelAvg - pfx.VelVar) * _velocityToMetersPerSecond);
            var maxSpeed = Mathf.Max(0f, (pfx.VelAvg + pfx.VelVar) * _velocityToMetersPerSecond);

            var min = Vector3.positiveInfinity;
            var max = Vector3.negativeInfinity;
            foreach (var head in new[] { pfx.DirAngleHead - headVar, pfx.DirAngleHead, pfx.DirAngleHead + headVar })
            {
                foreach (var elev in new[] { pfx.DirAngleElev - elevVar, pfx.DirAngleElev, pfx.DirAngleElev + elevVar })
                {
                    var headRad = (90f + head) * Mathf.Deg2Rad;
                    var elevRad = elev * Mathf.Deg2Rad;
                    var direction = new Vector3(Mathf.Cos(elevRad) * Mathf.Cos(headRad), Mathf.Sin(elevRad),
                        Mathf.Cos(elevRad) * Mathf.Sin(headRad));
                    foreach (var speed in new[] { minSpeed, maxSpeed })
                    {
                        min = Vector3.Min(min, direction * speed);
                        max = Vector3.Max(max, direction * speed);
                    }
                }
            }

            var mainModule = particleSystem.main;
            mainModule.startSpeed = 0f;

            var velocity = particleSystem.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.x = new ParticleSystem.MinMaxCurve(min.x, max.x);
            velocity.y = new ParticleSystem.MinMaxCurve(min.y, max.y);
            velocity.z = new ParticleSystem.MinMaxCurve(min.z, max.z);
        }

        /// <summary>
        /// The particles' material, shared per texture and blend mode. Like the engine: BLEND plain alpha blending, ADD
        /// adds the color (both without alpha test or premultiply - those made particles vanish below half alpha and a
        /// build render differently), MUL multiplies. Null if the texture doesn't exist.
        /// </summary>
        [CanBeNull]
        private Material GetMaterial(string textureName, string alphaFunc)
        {
            var key = $"{textureName}|{alphaFunc}".ToUpperInvariant();
            if (_materials.TryGetValue(key, out var cached) && cached != null)
                return cached;

            var material = new Material(Constants.ShaderUnlitParticles);
            _textureService.SetTexture(textureName, material);
            if (material.mainTexture == null)
            {
                UnityEngine.Object.Destroy(material);
                return null;
            }

            switch (alphaFunc?.ToUpperInvariant())
            {
                case "ADD":
                    material.ToParticleMode(UnityEngine.Rendering.BlendMode.SrcAlpha,
                        UnityEngine.Rendering.BlendMode.One);
                    break;
                case "MUL":
                    material.ToParticleMode(UnityEngine.Rendering.BlendMode.DstColor,
                        UnityEngine.Rendering.BlendMode.Zero);
                    break;
                default:
                    material.ToParticleMode(UnityEngine.Rendering.BlendMode.SrcAlpha,
                        UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    break;
            }

            // Both faces.
            material.SetInt("_Cull", (int)CullMode.Off);
            _materials[key] = material;
            return material;
        }
    }
}
