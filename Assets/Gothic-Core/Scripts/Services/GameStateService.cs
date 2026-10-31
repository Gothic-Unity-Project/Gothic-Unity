using System.Collections.Generic;
using System.Text;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Npc;
using Gothic.Core.Models.Dialog;
using Gothic.Core.Models.Vob.WayNet;
using Gothic.Core.Models.WayNet;
using ZenKit;
using ZenKit.Daedalus;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;
using WayPoint = Gothic.Core.Models.Vob.WayNet.WayPoint;

namespace Gothic.Core.Services
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

        // [zCMover] keyed by VOB Name — multiple movers can share the same name (copy-pasted VOBs)
        public readonly Dictionary<string, List<VobContainer>> VobsMover = new();

        // [oCTriggerScript] keyed by VOB Name — for Wld_SendTrigger and mob-grab trigger chains
        public readonly Dictionary<string, VobContainer> VobsTriggerScript = new();

        // [zCTriggerList] keyed by VOB Name — fires multiple targets in sequence/all/random
        public readonly Dictionary<string, VobContainer> VobsTriggerList = new();

        // [zCCodeMaster] keyed by VOB Name — combination/sequence puzzle controller
        public readonly Dictionary<string, VobContainer> VobsCodeMaster = new();

        // [zCTrigger] keyed by VOB Name — proximity zone that can also receive programmatic OnTrigger
        public readonly Dictionary<string, VobContainer> VobsTrigger = new();

        // [zCMoverController] keyed by VOB Name — sends keyframe commands to a target mover
        public readonly Dictionary<string, VobContainer> VobsMoverController = new();

        // [zCMessageFilter] keyed by VOB Name — converts OnTrigger/OnUntrigger to another action on its Target
        public readonly Dictionary<string, VobContainer> VobsMessageFilter = new();

        // zCEarthquake: the world shakes when it is triggered.
        public readonly Dictionary<string, VobContainer> VobsEarthquake = new();

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
            ClearTriggerVobs();

            Dialogs.Dispose();
        }

        /// <summary>
        /// Registers movers and trigger VOBs (root and child VOBs alike) in their by-name lookups.
        /// Does nothing for other VOB types or empty names.
        /// </summary>
        public void RegisterNamedVob(VobContainer container)
        {
            var name = container.Vob.Name;
            if (string.IsNullOrEmpty(name))
                return;

            switch (container.Vob.Type)
            {
                case VirtualObjectType.zCMover:
                    var moverKey = name.ToUpper();
                    if (!VobsMover.TryGetValue(moverKey, out var moverList))
                        VobsMover[moverKey] = moverList = new();
                    moverList.Add(container);
                    return;
                case VirtualObjectType.oCTriggerScript:
                    VobsTriggerScript[name.ToUpper()] = container;
                    break;
                case VirtualObjectType.zCTriggerList:
                    VobsTriggerList[name.ToUpper()] = container;
                    break;
                case VirtualObjectType.zCCodeMaster:
                    VobsCodeMaster[name.ToUpper()] = container;
                    break;
                case VirtualObjectType.zCTrigger:
                    VobsTrigger[name.ToUpper()] = container;
                    break;
                case VirtualObjectType.zCMoverController:
                    VobsMoverController[name.ToUpper()] = container;
                    break;
                case VirtualObjectType.zCMessageFilter:
                    VobsMessageFilter[name.ToUpper()] = container;
                    break;
                case VirtualObjectType.zCEarthquake:
                    VobsEarthquake[name.ToUpper()] = container;
                    break;
                default:
                    return;
            }

            Logger.Log($"[GameStateService] Registered {container.Vob.Type}: '{name}'", LogCat.Vob);
        }

        /// <summary>
        /// Movers and trigger VOBs are looked up by name - none of them may survive a world change.
        /// </summary>
        public void ClearTriggerVobs()
        {
            VobsMover.Clear();
            VobsTriggerScript.Clear();
            VobsTriggerList.Clear();
            VobsCodeMaster.Clear();
            VobsTrigger.Clear();
            VobsMoverController.Clear();
            VobsMessageFilter.Clear();
            VobsEarthquake.Clear();
        }
    }
}
