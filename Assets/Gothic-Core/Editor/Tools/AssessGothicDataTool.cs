using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using GUZ.Core.Adapters;
using GUZ.Core.Domain.Config;
using GUZ.Core.Extensions;
using GUZ.Core.Models.Config;
using GUZ.Core.Services.Caches;
using UnityEditor;
using UnityEngine;
using ZenKit;
using Object = UnityEngine.Object;

namespace GUZ.Core.Editor.Tools
{
    public class AssessGothicDataTool : EditorWindow
    {
        private static ResourceCacheService _resourceCache;
        private static DeveloperConfig _developerConfig;
        private static string _logString;
        
        [MenuItem("UnZENity/Debug/Collect Gothic Information", true)]
        private static bool ValidateShowInformation()
        {
            return !Application.isPlaying;
        }

        [MenuItem("UnZENity/Debug/Collect Gothic Information", false, priority = 110)]
        public static void ShowInformation()
        {
            _logString = "";
            _resourceCache = new ResourceCacheService();
            _developerConfig = Object.FindFirstObjectByType<BootstrapAdapter>().DeveloperConfig;

            if (_developerConfig.GameVersion == GameVersion.Gothic1)
                _resourceCache.Init(JsonRootLoader.DefaultSteamGothic1Folder);
            else
                _resourceCache.Init(@"C:\Program Files (x86)\Steam\steamapps\common\Gothic II - clean\");
                // _resourceCache.Init(JsonRootLoader.DefaultSteamGothic2Folder);

            Debug.Log("==========Worlds==========");
            var worlds = CollectWorlds();
            Debug.Log("==========World Materials and Textures==========");
            CollectWorldMaterialsAndTextures(worlds);
            Debug.Log("==========Textures==========");
            // PrintTextures();


            // Debug.Log(Application.dataPath);
            // File.WriteAllText(_logString);
        }
        
        private static Dictionary<string, World> CollectWorlds()
        {
            var worlds = new Dictionary<string, World>();
            FindAllWorlds(_resourceCache.Vfs.Root, worlds);
            Debug.Log("Found " + worlds.Count + " worlds");

            foreach (var keyValuePair in worlds)
            {
                Debug.Log($"{keyValuePair.Key}: " +
                          $"NodeCount={keyValuePair.Value.BspTree.NodeCount}, " +
                          $"LeaveNodeIndicesCount={keyValuePair.Value.BspTree.LeafNodeIndices.Count}, " +
                          $"SectorCount={keyValuePair.Value.BspTree.SectorCount}, " + 
                          $"LightPointsCount={keyValuePair.Value.BspTree.LightPointCount}, " +
                          $"MaterialCount={keyValuePair.Value.Mesh.MaterialCount}");
            }
            
            return worlds;
        }
        
        private static void FindAllWorlds(VfsNode node, Dictionary<string, World> worlds)
        {
            foreach (var child in node.Children)
            {
                if (child.Name.EndsWithIgnoreCase(".zen"))
                {
                    try
                    {
                        worlds.Add(child.Name, _resourceCache.TryGetWorld(child.Name, _developerConfig.GameVersion));
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"{child.Name}: {e}");
                    }
                }
                
                FindAllWorlds(child, worlds);
            }
        }
        
        private static void CollectWorldMaterialsAndTextures(Dictionary<string, World> worlds)
        {
            var materials = new Dictionary<string, IMaterial>();
            var textures = new Dictionary<string, ITexture>();

            foreach (var world in worlds)
            {
                for (uint i = 0; i < world.Value.Mesh.MaterialCount; i++)
                {
                    try
                    {
                        var material = world.Value.Mesh.GetMaterial((int)i);
                        if (material != null && !materials.ContainsKey(material.Name))
                        {
                            materials.Add(material.Name, material);

                            if (material.Texture != "" && !textures.ContainsKey(material.Texture))
                                textures.Add(material.Texture, _resourceCache.TryGetTexture(material.Texture));
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Failed to get material {i} from world {world.Key}: {e}");
                    }
                }
                
                
            }

            Debug.Log($"Found {materials.Count} unique materials");

            var materialGroups = materials
                .GroupBy(m => m.Value.Group)
                .Select(g => new { Group = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count);

            Debug.Log($"Material groups count: {materialGroups.Count()}");
            foreach (var group in materialGroups)
            {
                Debug.Log($"Material Group '{group.Group}': {group.Count} materials");
            }

            Debug.Log("First 10 materials:");
            foreach (var material in materials.Take(10))
            {
                Debug.Log($"Material: {material.Key}, Texture: {material.Value.Texture}, Group: {material.Value.Group}");
            }

            Debug.Log($"Found {textures.Count} unique textures from materials");
            Debug.Log("First 10 textures:");
            foreach (var texture in textures.Take(10))
            {
                Debug.Log($"Texture: {texture.Key}, Size: {texture.Value.Width}x{texture.Value.Height}");
            }
            
            var textureGroups = textures
                .GroupBy(t => new { Width = t.Value.Width, Height = t.Value.Height, group = t.Value.Format })
                .Select(g => new { g.Key.Width, g.Key.Height, Count = g.Count() })
                .OrderByDescending(g => g.Count);

            foreach (var group in textureGroups)
            {
                Debug.Log($"Material textures {group.Width}x{group.Height}: {group.Count}");
            }
        }

        private static void PrintTextures()
        {
            _ignoredTextures = 0;
            var textures = new Dictionary<string, ITexture>();

            FinaAllTextures(_resourceCache.Vfs.Root, textures);
            Debug.Log("Found " + textures.Count + $" textures. Ignored (as no texture found): {_ignoredTextures}");
            
            var emptyTextures = textures.Where(t => t.Value == null);
            Debug.Log("Empty textures found: " + emptyTextures.Count() + $" first 10: {string.Join(", ", emptyTextures.Take(10).Select(t => t.Key))}");
            emptyTextures.ToList().ForEach(t => Debug.Log($"{t.Key}: {t.Value}"));
            
            var textureGroups = textures
                .GroupBy(t => new { Width = t.Value.Width, Height = t.Value.Height })
                .Select(g => new { g.Key.Width, g.Key.Height, Count = g.Count() })
                .OrderByDescending(g => g.Count);

            foreach (var group in textureGroups)
            {
                Debug.Log($"Textures {group.Width}x{group.Height}: {group.Count}");
            }
        }

        private static int _ignoredTextures = 0;
        private static void FinaAllTextures(VfsNode node, Dictionary<string, ITexture> textures)
        {
            foreach (var child in node.Children)
            {
                if (child.Name.EndsWithIgnoreCase(".tex") || child.Name.EndsWithIgnoreCase(".tga"))
                {
                    var formattedName = child.Name.TrimEndIgnoreCase("-c.tex").TrimEndIgnoreCase(".tga");
                    
                    try
                    {
                        var texture = _resourceCache.TryGetTexture(formattedName);
                        if (texture == null)
                        {
                            _ignoredTextures++;
                        }
                        else
                        {
                            textures.TryAdd(formattedName, texture);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"{child.Name}: {e}");
                    }
                }
                
                FinaAllTextures(child, textures);
            }
        }
    }
}
