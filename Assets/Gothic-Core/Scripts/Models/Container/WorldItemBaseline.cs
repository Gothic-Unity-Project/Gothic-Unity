using System.Collections.Generic;
using ZenKit.Vobs;
using Vector3 = System.Numerics.Vector3;

namespace Gothic.Core.Models.Container
{
    /// <summary>
    /// DeveloperConfig.EnableWorldItemSaveState: a world's items and chests as its world file (.zen) has them. A save
    /// stores only what differs (taken, dropped or moved items, changed chests).
    /// </summary>
    public class WorldItemBaseline
    {
        public readonly Dictionary<IVirtualObject, string> ItemKeys = new();
        public readonly Dictionary<IVirtualObject, Vector3> ItemPositions = new();
        public readonly Dictionary<IVirtualObject, int> ItemAmounts = new();
        public readonly Dictionary<IVirtualObject, string> ContainerKeys = new();
        public readonly Dictionary<IVirtualObject, string> ContainerContents = new();
        public readonly Dictionary<IVirtualObject, bool> ContainerLocks = new();
    }
}
