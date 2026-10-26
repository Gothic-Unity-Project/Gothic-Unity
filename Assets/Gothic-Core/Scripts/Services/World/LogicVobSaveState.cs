using System.Collections.Generic;
using System.Linq;
using ZenKit.Vobs;

namespace Gothic.Core.Services.World
{
    /// <summary>
    /// DeveloperConfig.EnableLogicVobSaveState: runtime state of movers and triggers per world, kept for the session and
    /// written to UNITYSAVE.json. Before, a save reloaded every world with all movers closed - G2 Irdorath's pillars and
    /// the opened main door were back at the start, the player was locked in.
    ///
    /// Movers and trigger handlers take their entry themselves in Init (TryTake) - lazy loaded VOBs get theirs whenever
    /// they are created. Entries not taken yet stay pending and are saved again.
    /// </summary>
    public static class LogicVobSaveState
    {
        private static readonly Dictionary<string, Dictionary<string, LogicVobSaveEntry>> _byWorld = new();
        private static Dictionary<string, LogicVobSaveEntry> _pending = new();

        public static void Clear()
        {
            _byWorld.Clear();
            _pending = new Dictionary<string, LogicVobSaveEntry>();
        }

        public static void Import(Dictionary<string, List<LogicVobSaveEntry>> byWorld)
        {
            Clear();
            if (byWorld == null)
                return;

            foreach (var (world, entries) in byWorld)
                _byWorld[NormalizeWorld(world)] = entries.ToDictionary(e => e.Key, e => e);
        }

        public static Dictionary<string, List<LogicVobSaveEntry>> Export()
        {
            return _byWorld.ToDictionary(w => w.Key, w => w.Value.Values.ToList());
        }

        /// <summary>
        /// Before the VOBs of a world are created: its saved entries become the pending ones.
        /// </summary>
        public static int BeginWorld(string worldName)
        {
            _pending = _byWorld.TryGetValue(NormalizeWorld(worldName), out var entries)
                ? new Dictionary<string, LogicVobSaveEntry>(entries)
                : new Dictionary<string, LogicVobSaveEntry>();
            return _pending.Count;
        }

        /// <summary>
        /// The current world's state: the live entries plus the pending ones whose VOB wasn't created yet.
        /// </summary>
        public static void StoreWorld(string worldName, IEnumerable<LogicVobSaveEntry> liveEntries)
        {
            var entries = new Dictionary<string, LogicVobSaveEntry>(_pending);
            foreach (var entry in liveEntries)
                entries[entry.Key] = entry;
            _byWorld[NormalizeWorld(worldName)] = entries;
        }

        public static bool TryTake(IVirtualObject vob, out LogicVobSaveEntry entry)
        {
            return _pending.Remove(MakeKey(vob), out entry);
        }

        /// <summary>
        /// Type, name and position from the world file - names repeat (G2 Irdorath: EVT_LEFT_ROOM_01_SECRETWALL_01 twice).
        /// </summary>
        public static string MakeKey(IVirtualObject vob)
        {
            var pos = vob.Position;
            return $"{vob.Type}|{vob.Name}|{(int)pos.X},{(int)pos.Y},{(int)pos.Z}";
        }

        private static string NormalizeWorld(string worldName)
        {
            var name = (worldName ?? string.Empty).ToUpperInvariant();
            return name.EndsWith(".ZEN") ? name : name + ".ZEN";
        }
    }
}
