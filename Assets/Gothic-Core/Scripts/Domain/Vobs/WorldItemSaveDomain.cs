using System;
using System.Collections.Generic;
using System.Linq;
using Gothic.Core.Extensions;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.World;
using ZenKit.Vobs;
using Quaternion = UnityEngine.Quaternion;
using Vector3 = System.Numerics.Vector3;

namespace Gothic.Core.Domain.Vobs
{
    /// <summary>
    /// DeveloperConfig.EnableWorldItemSaveState: a world is built from its world file (.zen) on load - the items lying
    /// in it and the chests' contents are stored as the difference to it. A beer taken before saving was in the
    /// backpack and in the world again after loading.
    /// World file objects are keyed by name and position (like LogicVobSaveState), stable across sessions and mod
    /// updates that don't move them.
    /// </summary>
    public static class WorldItemSaveDomain
    {
        // ZenKit positions are centimeters. Closer to its world file spot an item counts as not moved (physics settling,
        // float noise).
        private const float _movedDistance = 10f;

        /// <summary>
        /// Call on the world file's VOB list before anything changes it.
        /// </summary>
        public static WorldItemBaseline CreateBaseline(List<IVirtualObject> vobs)
        {
            var baseline = new WorldItemBaseline();
            var usedKeys = new Dictionary<string, int>();
            foreach (var vob in vobs)
            {
                if (vob is IItem item)
                {
                    baseline.ItemKeys[vob] = MakeUniqueKey(GetInstanceName(item), vob.Position, usedKeys);
                    baseline.ItemPositions[vob] = vob.Position;
                    baseline.ItemAmounts[vob] = Math.Max(1, item.Amount);
                }
                else if (vob is IContainer container)
                {
                    baseline.ContainerKeys[vob] = MakeUniqueKey($"CHEST:{vob.Name}", vob.Position, usedKeys);
                    baseline.ContainerContents[vob] = container.Contents ?? string.Empty;
                    baseline.ContainerLocks[vob] = container.IsLocked;
                }
            }
            return baseline;
        }

        /// <summary>
        /// The saved difference onto the freshly built world: taken/moved items out, dropped ones in, chests as left.
        /// </summary>
        public static void Apply(List<IVirtualObject> vobs, WorldItemBaseline baseline, WorldItemsSaveEntry state,
            out int removedCount, out int addedCount, out int containerCount)
        {
            removedCount = 0;
            addedCount = 0;
            containerCount = 0;

            if (state.RemovedItems is { Count: > 0 })
            {
                var removedKeys = new HashSet<string>(state.RemovedItems);
                foreach (var (vob, key) in baseline.ItemKeys)
                {
                    if (removedKeys.Contains(key) && vobs.Remove(vob))
                        removedCount++;
                }
            }

            if (state.Containers is { Count: > 0 })
            {
                var containersByKey = baseline.ContainerKeys.ToDictionary(i => i.Value, i => i.Key);
                foreach (var entry in state.Containers)
                {
                    if (!containersByKey.TryGetValue(entry.Key, out var vob) || vob is not IContainer container)
                        continue;

                    container.Contents = entry.Contents ?? string.Empty;
                    container.IsLocked = entry.IsLocked;
                    containerCount++;
                }
            }

            if (state.AddedItems is { Count: > 0 })
            {
                foreach (var entry in state.AddedItems)
                {
                    if (string.IsNullOrEmpty(entry.Instance) || entry.Position is not { Length: 3 })
                        continue;

                    vobs.Add(CreateItemVob(entry));
                    addedCount++;
                }
            }
        }

        /// <summary>
        /// The world's current items/chests against its world file. Item positions have to be up to date (synced from
        /// their GameObjects) for the current world.
        /// </summary>
        public static WorldItemsSaveEntry Capture(List<IVirtualObject> vobs, WorldItemBaseline baseline)
        {
            var state = new WorldItemsSaveEntry();
            var present = new HashSet<IVirtualObject>(vobs);

            foreach (var (vob, key) in baseline.ItemKeys)
            {
                if (!present.Contains(vob))
                {
                    state.RemovedItems.Add(key);
                    continue;
                }

                // Moved, or part of a stack used up (one of three beers drunk): away from the world file spot, added as
                // it is now.
                var item = (IItem)vob;
                if (Vector3.Distance(vob.Position, baseline.ItemPositions[vob]) > _movedDistance ||
                    Math.Max(1, item.Amount) != baseline.ItemAmounts[vob])
                {
                    state.RemovedItems.Add(key);
                    state.AddedItems.Add(CreateEntry(item, vob));
                }
            }

            foreach (var vob in vobs)
            {
                if (vob is IItem item && !baseline.ItemKeys.ContainsKey(vob))
                    state.AddedItems.Add(CreateEntry(item, vob));
            }

            foreach (var (vob, key) in baseline.ContainerKeys)
            {
                var container = (IContainer)vob;
                var contents = container.Contents ?? string.Empty;
                if (contents == baseline.ContainerContents[vob] && container.IsLocked == baseline.ContainerLocks[vob])
                    continue;

                state.Containers.Add(new ContainerSaveEntry
                {
                    Key = key,
                    Contents = contents,
                    IsLocked = container.IsLocked
                });
            }

            return state;
        }

        public static string GetInstanceName(IItem item)
        {
            return !string.IsNullOrEmpty(item.Instance) ? item.Instance : item.Name;
        }

        private static string MakeUniqueKey(string name, Vector3 position, Dictionary<string, int> usedKeys)
        {
            var key = $"{name?.ToUpperInvariant()}|{(int)Math.Round(position.X)}|{(int)Math.Round(position.Y)}|" +
                      $"{(int)Math.Round(position.Z)}";
            // Two of the same at one spot (a stack placed twice): numbered in world file order.
            usedKeys.TryGetValue(key, out var count);
            usedKeys[key] = count + 1;
            return count == 0 ? key : $"{key}#{count}";
        }

        private static WorldItemSaveEntry CreateEntry(IItem item, IVirtualObject vob)
        {
            var rotation = vob.Rotation.ToUnityQuaternion();
            return new WorldItemSaveEntry
            {
                Instance = GetInstanceName(item),
                Amount = Math.Max(1, item.Amount),
                Position = new[] { vob.Position.X, vob.Position.Y, vob.Position.Z },
                Rotation = new[] { rotation.x, rotation.y, rotation.z, rotation.w }
            };
        }

        private static IVirtualObject CreateItemVob(WorldItemSaveEntry entry)
        {
            var rotation = entry.Rotation is { Length: 4 }
                ? new Quaternion(entry.Rotation[0], entry.Rotation[1], entry.Rotation[2], entry.Rotation[3])
                : Quaternion.identity;
            return new Item
            {
                Name = entry.Instance,
                Instance = entry.Instance,
                Amount = Math.Max(1, entry.Amount),
                Position = new Vector3(entry.Position[0], entry.Position[1], entry.Position[2]),
                Rotation = rotation.ToZkMatrix(),
                Visual = new VisualMesh()
            };
        }
    }
}
