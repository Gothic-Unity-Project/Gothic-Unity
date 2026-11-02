using System.Collections.Generic;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Const;
using Gothic.Core.Logging;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.StaticCache;
using Gothic.Core.Services.World;
using Reflex.Attributes;
using UnityEngine;
using ZenKit;
using Logger = Gothic.Core.Logging.Logger;
using Mesh = UnityEngine.Mesh;

namespace Gothic.Core.Services.Audio
{
    /// <summary>
    /// DeveloperConfig.EnableFootstepSounds: the ground's sound material for a step (*eventSFXGrnd "Run" -> RUN_WOOD)
    /// like the engine (OpenGothic Npc::emitSoundGround: the material group of the polygon below). Our world mesh is
    /// baked into chunks, but every vertex keeps its texture array index (uv0.z) - the index gives the texture, the
    /// world's materials its group (Ghorri on the jetty: wood, not earth). Wading is water; a VOB below or a texture we
    /// don't know falls back to stone under a ceiling, earth outside.
    /// </summary>
    public class GroundSoundService
    {
        [Inject] private readonly StaticCacheService _staticCacheService;
        [Inject] private readonly SaveGameService _saveGameService;
        [Inject] private readonly NpcWaterService _npcWaterService;
        [Inject] private readonly Config.ConfigService _configService;

        private const float _groundRayStart = 0.3f;
        private const float _groundRayLength = 1.5f;
        private const float _wadingDepth = 0.15f;
        private const string _transparentChunkPrefix = "Transparent";

        // Per world: texture array index -> material group; per chunk mesh: its triangles and texture indices.
        private Dictionary<int, MaterialGroup> _opaqueGroups;
        private Dictionary<int, MaterialGroup> _transparentGroups;
        private Dictionary<int, string> _opaqueTextures;
        private Dictionary<int, string> _transparentTextures;
        private readonly Dictionary<Mesh, (int[] Triangles, List<Vector4> Uvs)> _meshData = new();
        private string _worldName;

        public GroundSoundService()
        {
            GlobalEventDispatcher.WorldSceneLoaded.AddListener(Reset);
        }

        public string GetSoundName(string baseName, Vector3 feet)
        {
            return $"{baseName}_{GetMaterial(feet)}";
        }

        public string GetMaterial(Vector3 feet)
        {
            if (_npcWaterService != null &&
                _npcWaterService.TryGetWaterSurface(feet + Vector3.up * _groundRayStart, out var groundY, out var waterY) &&
                waterY - groundY >= _wadingDepth && waterY > feet.y)
                return "WATER";

            if (Physics.Raycast(feet + Vector3.up * _groundRayStart, Vector3.down, out var hit, _groundRayLength,
                    1 << Constants.DefaultLayer, QueryTriggerInteraction.Ignore))
            {
                var suffix = GetTextureSoundSuffix(hit);
                if (suffix != null)
                    return suffix;
                if (hit.collider.GetComponentInParent<VobLoader>() != null)
                    return "WOOD";
            }

            return Domain.Audio.GroundSoundDomain.IsUnderWorldCeiling(feet) ? "STONE" : "EARTH";
        }

        /// <summary>
        /// The texture of the triangle below (world chunks and VOBs both keep their texture array index in uv0.z): its
        /// world material group, or - G1's world has almost only UNDEF groups (2246 of 2263 materials), the engine then
        /// plays RUN_UNDEF (stone) on grass too - DeveloperConfig.EnableGroundTextureGuess guesses it from the texture's
        /// name (OWODFLGRASSMI: grass = earth, MOWOBARREL01: wood).
        /// </summary>
        private string GetTextureSoundSuffix(RaycastHit hit)
        {
            if (hit.collider is not MeshCollider meshCollider || meshCollider.sharedMesh == null ||
                hit.triangleIndex < 0)
                return null;

            var name = meshCollider.gameObject.name;
            var isTransparent = name.StartsWith(_transparentChunkPrefix);

            EnsureGroups();
            if (_opaqueGroups == null)
                return null;

            var mesh = meshCollider.sharedMesh;
            if (!_meshData.TryGetValue(mesh, out var data))
            {
                if (!mesh.isReadable)
                    return null;
                var uvs = new List<Vector4>();
                mesh.GetUVs(0, uvs);
                data = (mesh.triangles, uvs);
                _meshData[mesh] = data;
            }

            var vertex = hit.triangleIndex * 3;
            if (vertex >= data.Triangles.Length || data.Triangles[vertex] >= data.Uvs.Count)
                return null;
            var textureIndex = Mathf.RoundToInt(data.Uvs[data.Triangles[vertex]].z);
            var textures = isTransparent ? _transparentTextures : _opaqueTextures;
            if (!textures.TryGetValue(textureIndex, out var texture))
                return null;

            var groups = isTransparent ? _transparentGroups : _opaqueGroups;
            if (groups.TryGetValue(textureIndex, out var group) && group != MaterialGroup.Undefined)
                return ToSoundSuffix(group);
            return _configService.Dev.EnableGroundTextureGuess ? GuessSoundSuffix(texture) : "UNDEF";
        }

        // DeveloperConfig.EnableGroundTextureGuess: German and English parts of Gothic's texture names.
        private static readonly (string Suffix, string[] Parts)[] _textureGuesses =
        {
            ("WATER", new[] { "WATER", "WASSER", "WAT_" }),
            ("WOOD", new[] { "WOOD", "HOLZ", "PLANK", "BRETT", "BARREL", "FASS", "BOARD", "BALKEN", "WO_", "MOWO" }),
            ("METAL", new[] { "METAL", "EISEN", "IRON", "STAHL" }),
            ("SAND", new[] { "SAND", "BEACH", "STRAND" }),
            ("EARTH", new[] { "GRASS", "GRAS", "EARTH", "ERDE", "MUD", "SCHLAMM", "MOOS", "MOSS", "LEAF", "BLATT",
                "DIRT", "DRECK", "WEG", "PATH", "FOREST", "WALD", "GROUND", "BODEN" }),
            ("STONE", new[] { "STONE", "STEIN", "ROCK", "FELS", "WALL", "MAUER", "BRICK", "CAVE", "HOEHLE", "TILE",
                "MARBLE", "MARMOR" })
        };

        private static string GuessSoundSuffix(string texture)
        {
            var name = texture.ToUpperInvariant();
            foreach (var (suffix, parts) in _textureGuesses)
            {
                foreach (var part in parts)
                {
                    if (name.Contains(part))
                        return suffix;
                }
            }
            return "UNDEF";
        }

        private void EnsureGroups()
        {
            if (_opaqueGroups != null && _worldName == _saveGameService.CurrentWorldName)
                return;

            _worldName = _saveGameService.CurrentWorldName;
            var textureGroups = new Dictionary<string, MaterialGroup>(System.StringComparer.OrdinalIgnoreCase);
            var materials = _saveGameService.CurrentWorldData?.Mesh?.Materials;
            if (materials != null)
            {
                foreach (var material in materials)
                {
                    if (!string.IsNullOrEmpty(material.Texture))
                        textureGroups.TryAdd(material.Texture, material.Group);
                }
            }

            _opaqueGroups = ToIndexGroups(_staticCacheService.LoadedTextureInfoOpaque, textureGroups);
            _transparentGroups = ToIndexGroups(_staticCacheService.LoadedTextureInfoTransparent, textureGroups);
            _opaqueTextures = ToIndexTextures(_staticCacheService.LoadedTextureInfoOpaque);
            _transparentTextures = ToIndexTextures(_staticCacheService.LoadedTextureInfoTransparent);
            Logger.Log($"[GroundSound] {_worldName}: {textureGroups.Count} textures, {_opaqueGroups.Count} opaque and " +
                       $"{_transparentGroups.Count} transparent with a material group", LogCat.Audio);
        }

        private static Dictionary<int, string> ToIndexTextures(
            Dictionary<string, (int Index, StaticCacheService.TextureInfo Data)> textures)
        {
            var result = new Dictionary<int, string>();
            if (textures == null)
                return result;
            foreach (var (texture, info) in textures)
                result[info.Index] = texture;
            return result;
        }

        private static Dictionary<int, MaterialGroup> ToIndexGroups(
            Dictionary<string, (int Index, StaticCacheService.TextureInfo Data)> textures,
            Dictionary<string, MaterialGroup> textureGroups)
        {
            var groups = new Dictionary<int, MaterialGroup>();
            if (textures == null)
                return groups;
            foreach (var (texture, info) in textures)
            {
                if (textureGroups.TryGetValue(texture, out var group))
                    groups[info.Index] = group;
            }
            return groups;
        }

        /// <summary>
        /// The engine's names of the material groups (C_SFX Run_Earth, Run_Wood, ...).
        /// </summary>
        private static string ToSoundSuffix(MaterialGroup group)
        {
            return group switch
            {
                MaterialGroup.Metal => "METAL",
                MaterialGroup.Stone => "STONE",
                MaterialGroup.Wood => "WOOD",
                MaterialGroup.Water => "WATER",
                MaterialGroup.Earth => "EARTH",
                MaterialGroup.Snow => "EARTH",
                _ => "UNDEF"
            };
        }

        private void Reset()
        {
            _opaqueGroups = null;
            _transparentGroups = null;
            _meshData.Clear();
        }
    }
}
