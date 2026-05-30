using System.Collections.Generic;
using System.Text;
using GUZ.Core.Models.Container;
using GUZ.Core.Models.Dialog;
using GUZ.Core.Models.Npc;
using GUZ.Core.Models.Vob.WayNet;
using GUZ.Core.Models.WayNet;
using ZenKit;
using ZenKit.Daedalus;
using WayPoint = GUZ.Core.Models.Vob.WayNet.WayPoint;

namespace GUZ.Core.Services
{
    public class GameStateService
    {
        /// <summary>
        /// Represents the currently installed Gothic language (windows-1250,1251,1252)
        /// </summary>
        public Encoding Encoding;
        public string Language;
        public DaedalusVm GothicVm;
        public DaedalusVm FightVm;
        public DaedalusVm MenuVm;
        public DaedalusVm SfxVm; // Sound FX
        public DaedalusVm PfxVm; // Particle FX

        // Lookup optimized WayNet data
        public readonly Dictionary<string, WayPoint> WayPoints = new();
        public readonly Dictionary<string, FreePoint> FreePoints = new();

        // Reorganized waypoints from world data.
        public Dictionary<string, DijkstraWaypoint> DijkstraWaypoints = new();
        
        // [IInteractiveObject] => VisualScheme (aka vob.Visual.Name.SubString("_");
        public readonly Dictionary<string, List<VobContainer>> VobsInteractable = new();

        public int GuildHumanCount;
        public int GuildCount;
        public int[] GuildAttitudes;

        public readonly DialogModel Dialogs = new();

        public GuildValuesInstance GuildValues;

        // FIXME Find a better place for the NPC routines. E.g. on the NPCs itself? But we e.g. need to have a NPCObject List to do so.
        public Dictionary<int, List<RoutineData>> NpcRoutines = new();

        public bool InGameAndAlive = false;

        // FIXME - Need to be called when a new world is loaded!
        public void Reset()
        {
            WayPoints.Clear();
            FreePoints.Clear();
            VobsInteractable.Clear();
        }

        public void Dispose()
        {
            // Needs to be reset as Unity won't clear variables when closing game in EditorMode.
            GothicVm = null;
            SfxVm = null;
            WayPoints.Clear();
            FreePoints.Clear();
            VobsInteractable.Clear();

            Dialogs.Dispose();
        }
    }
}
