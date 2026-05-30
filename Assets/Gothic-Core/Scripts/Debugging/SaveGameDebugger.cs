using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GUZ.Core.Logging;
using GUZ.Core.Services.World;
using MyBox;
using Reflex.Attributes;
using UnityEngine;
using ZenKit;
using ZenKit.Util;
using ZenKit.Vobs;
using Logger = GUZ.Core.Logging.Logger;
using Vector3 = System.Numerics.Vector3;

namespace GUZ.Core.Debugging
{
    public class SaveGameDebugger : MonoBehaviour
    {
        [Separator("Save")] public bool DoSaveGame;
        [Range(1, 15)] public int SaveSlot = 15;


        [Separator("Compare")] public bool CompareSaveGames;
        [Range(1, 15)] public int SaveSlot1ToCompare = 1;
        [Range(1, 15)] public int SaveSlot2ToCompare = 15;
        public string WorldToCompare = "WORLD.zen";

        
        [Inject] private readonly SaveGameService _saveGameService;


        private void OnValidate()
        {
            if (DoSaveGame)
            {
                DoSaveGame = false;
                StartCoroutine(ExecuteSave());
            }

            if (CompareSaveGames)
            {
                CompareSaveGames = false;
                CompareSaves();
            }
        }

        /// <summary>
        /// We need to start saving at the end of the frame so that a Screenshot can be taken. Otherwise, it's null.
        /// </summary>
        private IEnumerator ExecuteSave()
        {
            yield return new WaitForEndOfFrame();

            _saveGameService.SaveCurrentGame((SaveGameService.SlotId)SaveSlot, $"UnZENity-DEBUG - {DateTime.Now}");

            Logger.LogEditor("DONE", LogCat.Debug);
        }

        private void CompareSaves()
        {
            var save1 = _saveGameService.GetSaveGame((SaveGameService.SlotId)SaveSlot1ToCompare)!;
            var save2 = _saveGameService.GetSaveGame((SaveGameService.SlotId)SaveSlot2ToCompare)!;

            var world1 = save1.LoadWorld(WorldToCompare)!;
            var world2 = save2.LoadWorld(WorldToCompare)!;
            Logger.LogEditor(
                $"Comparing >{WorldToCompare}< in save slots >{save1.Metadata.Title}< and >{save2.Metadata.Title}<.",
                LogCat.Debug);

            var vobsByTypeA = world1.RootObjects.GroupBy(i => i.Type).ToDictionary(i => i.Key, i => i.ToList());
            var vobsByTypeB = world2.RootObjects.GroupBy(i => i.Type).ToDictionary(i => i.Key, i => i.ToList());

            Logger.LogEditor("Comparing VOB counts", LogCat.Debug);
            {
                foreach (var countA in vobsByTypeA)
                {
                    Logger.LogEditor("---------", LogCat.Debug);
                    Logger.LogEditor($"### Checking VOB type >{countA.Key}<", LogCat.Debug);

                    if (!vobsByTypeB.Keys.Contains(countA.Key))
                    {
                        Logger.LogErrorEditor($"VOB type {countA.Key} is missing in slotB.", LogCat.Debug);
                    }
                    else
                    {
                        ComparePropertiesByType(vobsByTypeA[countA.Key], vobsByTypeB[countA.Key]);
                    }
                }

                // Check if there are any VobTypes which aren't in SaveGame1, but SaveGame2
                vobsByTypeB
                    .Where(i => !vobsByTypeA.Keys.Contains(i.Key))
                    .ToList()
                    .ForEach(i => Logger.LogErrorEditor($"VOBs of type >{i.Key}< are missing in slotB.", LogCat.Debug));
            }

            return;

            Logger.LogEditor("--------------------", LogCat.Debug);
            Logger.LogEditor("Compare close NPCs (excluding Monster)", LogCat.Debug);
            {
                var npcsA = world1.RootObjects.Where(i => i.Type == VirtualObjectType.oCNpc)
                    .Select(i => (Npc)i).ToList();
                var npcsB = world2.RootObjects.Where(i => i.Type == VirtualObjectType.oCNpc)
                    .Select(i => (Npc)i).ToList();

                foreach (var npcA in npcsA)
                {
                    // Skip monsters but not if it's the hero
                    if (npcA.Id <= 0 && !npcA.Player)
                    {
                        continue;
                    }

                    var npcB = npcsB.FirstOrDefault(i => i.Name == npcA.Name);

                    if (npcB == null)
                    {
                        Logger.LogErrorEditor($"NPC {npcA.Name} not found in saveB.", LogCat.Debug);
                        continue;
                    }

                    var distance = Vector3.Distance(npcA.Position, npcB.Position);
                    Debug.Assert(Vector3.Distance(npcA.Position, npcB.Position) < 5f,
                        $"NPC {npcA.Name} position isn't similar with >{distance}<.");

                    Debug.Assert(npcA.Lp == npcB.Lp, $"NPC {npcA.Name} Lp does not match.");
                    Debug.Assert(npcA.Level == npcB.Level, $"NPC {npcA.Name} level does not match.");
                    Debug.Assert(npcA.Xp == npcB.Xp, $"NPC {npcA.Name} Xp does not match.");
                    Debug.Assert(npcA.XpNextLevel == npcB.XpNextLevel, $"NPC {npcA.Name} XpNextLevel does not match.");
                    Debug.Assert(npcA.GetMission(0) == npcB.GetMission(0),
                        $"NPC {npcA.Name} Mission(0) does not match.");
                    Debug.Assert(npcA.GetAttribute(2) == npcB.GetAttribute(2),
                        $"NPC {npcA.Name} Attribute(2) does not match.");
                    Debug.Assert(npcA.Guild == npcB.Guild, $"NPC {npcA.Name} Guild does not match.");
                    Debug.Assert(npcA.GuildTrue == npcB.GuildTrue, $"NPC {npcA.Name} GuildTrue does not match.");
                }
            }

            Logger.LogEditor("--------------------", LogCat.Debug);
            Logger.LogEditor("Compare far away NPCs (excluding Monster)", LogCat.Debug);
            {

            }

            Logger.LogEditor("--------------------", LogCat.Debug);
            Logger.LogEditor("Comparing oCItem VOBs", LogCat.Debug);
            {
                var vobsA = world1.RootObjects.Where(i => i.Type == VirtualObjectType.oCItem).ToList();
                var vobsB = world2.RootObjects.Where(i => i.Type == VirtualObjectType.oCItem).ToList();

                Debug.Assert(vobsA.Count == vobsB.Count,
                    $"oCItem VOBs inside RootObjects do not match - slotA={vobsA.Count}, slotB={vobsB.Count}.");

                for (var i = 0; i < vobsB.Count; i++)
                {
                    var vobA = vobsA[i];
                    var vobB = vobsB[i];

                    Debug.Assert(vobA.Position == vobB.Position, $"VOB {vobA.Name} position does not match.");
                    Debug.Assert(vobA.Rotation == vobB.Rotation, $"VOB {vobA.Name} rotation does not match.");
                    // Debug.Assert(vob.Scale == vob2.Scale, $"VOB {vob.Name} scale does not match.");
                }
            }

            Logger.LogEditor("--------------------", LogCat.Debug);
            Logger.LogEditor("Comparing static VOBs (excluding NPCs+Monsters+Items)", LogCat.Debug);
            {
                var vobsA = world1.RootObjects
                    .Where(i => i.Type != VirtualObjectType.oCNpc && i.Type != VirtualObjectType.oCItem).ToList();
                var vobsB = world2.RootObjects
                    .Where(i => i.Type != VirtualObjectType.oCNpc && i.Type != VirtualObjectType.oCItem).ToList();

                Debug.Assert(vobsA.Count == vobsB.Count,
                    $"static VOBs inside RootObjects do not match - slotA={vobsA.Count}, slotB={vobsB.Count}.");

                for (var i = 0; i < vobsB.Count; i++)
                {
                    var vobA = vobsA[i];
                    var vobB = vobsB[i];

                    Debug.Assert(vobA.Position == vobB.Position, $"VOB {vobA.Name} position does not match.");
                    Debug.Assert(vobA.Rotation == vobB.Rotation, $"VOB {vobA.Name} rotation does not match.");
                }
            }
        }


        // Properties which we won't compare.
        private Dictionary<string, List<string>> _elementsToIgnore = new()
        {
            {
                typeof(VirtualObject).FullName!, new()
                {
                    nameof(VirtualObject.Id), // Includes the Index of current VM. Different for G1 and UnZENity
                    // TODO - It defines recurring events like heat damage over time (every x-milliseconds).
                    // TODO - Currently not handled within UnZENity, but could be useful in the future.
                    nameof(VirtualObject.NextOnTimer)
                }
            },
            {
                typeof(ZoneMusic).FullName!, new()
                {
                    //  Both *Done properties handle some initial loading of music files in G1, but the actual .sgt file
                    // isn't store in save file, therefore we ignore set/get it in UnZENity.
                    nameof(ZoneMusic.DayEntranceDone),
                    nameof(ZoneMusic.NightEntranceDone)
                }
            },
            {
                typeof(Mover).FullName!, new()
                {
                    // Seems like a physics topic for G1. We handle the speed of animations and their colliders differently in UnZENity.
                    nameof(Mover.MoveSpeedUnit),

                    // Only FREEMINEGATE has wrong values, ignoring for now
                    nameof(Mover.LerpType),
                    nameof(Mover.SpeedType)
                }
            }
        };

        private void ComparePropertiesByType(List<IVirtualObject> slotA, List<IVirtualObject> slotB)
        {
            if (slotA.Count == slotB.Count)
            {
                Logger.LogEditor($"VOB count matches: type={slotA.FirstOrDefault()?.Type} slotA={slotA.Count}, slotB={slotB.Count}.", LogCat.Debug);
            }
            else
            {
                Logger.LogErrorEditor($"VOB count does not match: type={slotA.FirstOrDefault()?.Type} slotA={slotA.Count}, slotB={slotB.Count}.", LogCat.Debug);
                return;
            }

            for (var i = 0; i < slotA.Count; i++)
            {
                var objectName = slotA[i].Name;

                // We loop through all the types from top to bottom.
                // We basically check all Properties except the ones we specifically exclude or handle manually.
                var properties = slotA[i].GetType().GetProperties();
                foreach (var property in properties)
                {
                    var declaringType = property.DeclaringType!.ToString();
                    var propertyName = property.Name;

                    if (_elementsToIgnore.TryGetValue(declaringType, out var typeToIgnore))
                    {
                        if (typeToIgnore.Contains(propertyName))
                        {
                            continue; // Ignore - not needed to be checked.
                        }
                    }

                    var valueA = property.GetValue(slotA[i]);
                    var valueB = property.GetValue(slotB[i]);

                    // Some PresetNames are empty on Gothic1, but exists on UnZNity (e.g. type=DOOR, PresetName=DOOR_WOODEN (on slotB only))
                    // Should be safe to ignore
                    if (property.Name == "PresetName" && ((string)valueA).IsNullOrEmpty())
                    {
                        continue;
                    }

                    switch (property.GetMethod.ReturnParameter!.ToString().Trim())
                    {
                        case "System.Collections.Generic.List`1[ZenKit.Vobs.IVirtualObject]":
                            var listA = (List<IVirtualObject>)valueA;
                            var listB = (List<IVirtualObject>)valueB;

                            if (listA.NotNullOrEmpty())
                            {
                                Logger.LogEditor($"### Checking VOB Children of {objectName}", LogCat.Debug);
                                ComparePropertiesByType(listA, listB);
                            }
                            // Else - all good
                            break;
                        // TriggerListTarget isn't extending IVirtualObject, we therefore check their values manually.
                        case "System.Collections.Generic.List`1[ZenKit.Vobs.ITriggerListTarget]":
                            var triggerListA = (List<ITriggerListTarget>)valueA;
                            var triggerListB = (List<ITriggerListTarget>)valueB;

                            Debug.Assert(triggerListA.Count == triggerListB.Count,
                                $"VOB property >{property.Name}< of type >{nameof(ITriggerListTarget)}< does not match: slotA={triggerListA.Count}, slotB={triggerListB.Count}");
                            for (var j = 0; j < triggerListA.Count; j++)
                            {  
                                Debug.Assert(triggerListA[j].Name == triggerListB[j].Name,
                                    $"VOB property >{property.Name}< of type >{nameof(ITriggerListTarget)}< does not match: slotA={triggerListA[j].Name}, slotB={triggerListB[j].Name}");
                                Debug.Assert(triggerListA[j].Delay == triggerListB[j].Delay,
                                    $"VOB property >{property.Name}< of type >{nameof(ITriggerListTarget)}< does not match: slotA={triggerListA[j].Delay}, slotB={triggerListB[j].Delay}");
                            }
                            break;
                        case "System.Collections.Generic.List`1[ZenKit.AnimationSample]":
                                var animationSamplesA = (List<AnimationSample>)valueA;
                                var animationSamplesB = (List<AnimationSample>)valueB;
                                Debug.Assert(animationSamplesA.Count == animationSamplesB.Count,
                                    $"VOB property >{property.Name}< of type >{nameof(AnimationSample)}< does not match: slotA={animationSamplesA.Count}, slotB={animationSamplesB.Count}");
                                for (var j = 0; j < animationSamplesA.Count; j++)
                                {
                                    Debug.Assert(animationSamplesA[j].Position == animationSamplesB[j].Position,
                                        $"VOB property >Position< of type >{nameof(AnimationSample)}< does not match: slotA={animationSamplesA[j].Position}, slotB={animationSamplesB[j].Position}");
                                    Debug.Assert(animationSamplesA[j].Rotation == animationSamplesB[j].Rotation,
                                        $"VOB property >Rotation< of type >{nameof(AnimationSample)}< does not match: slotA={animationSamplesA[j].Rotation}, slotB={animationSamplesB[j].Rotation}");
                                }
                                break;
                        case "ZenKit.Vobs.IVisual":
                            var visualA = (IVisual)valueA;
                            var visualB = (IVisual)valueB;

                            Debug.Assert(visualA?.Type == visualB?.Type,
                                $"VOB property >{property.Name}< of type >{nameof(IVisual)}< does not match: slotA={visualA?.Type}, slotB={visualB?.Type}");
                            Debug.Assert(visualA?.Name == visualB?.Name,
                                $"VOB property >{property.Name}< of type >{nameof(IVisual)}< does not match: slotA={visualA?.Name}, slotB={visualB?.Name}");
                            break;
                        case "ZenKit.Util.Matrix3x3":
                            var rotationA = (Matrix3x3)valueA;
                            var rotationB = (Matrix3x3)valueB;

                            // Identity comparison doesn't work. Therefore, we use a string comparison. (Please don't judge ;-D )
                            var vobAMatrix =
                                $"{rotationA.M11}, {rotationA.M12}, {rotationA.M13}, {rotationA.M21}, {rotationA.M22}, {rotationA.M23}, {rotationA.M31}, {rotationA.M32}, {rotationA.M33}";
                            var vobBMatrix =
                                $"{rotationB.M11}, {rotationB.M12}, {rotationB.M13}, {rotationB.M21}, {rotationB.M22}, {rotationB.M23}, {rotationB.M31}, {rotationB.M32}, {rotationB.M33}";
                            Debug.Assert(vobAMatrix == vobBMatrix,
                                $"VOB property >{property.Name}< of type >{nameof(Matrix3x3)}< does not match: slotA={vobAMatrix}, slotB={vobBMatrix}");

                            break;
                        default:
                            // Last but not least, normal check of values.
                            Debug.Assert(valueA == null && valueB == null || valueA.Equals(valueB),
                                $"{slotA[i].Name}: Property >{property.DeclaringType}.{property.Name}< does not match slotA=>{valueA}<, slotB=>{valueB}<");
                            break;
                    }
                }
            }
        }
    }
}
