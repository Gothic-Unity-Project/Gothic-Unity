using System;
using System.Collections.Generic;

namespace GUZ.Core.Models.Config
{
    public class DeveloperConfigEnums
    {
        public enum WorldToSpawn
        {
            None,
            // G1
            G1World,
            G1OldMine,
            G1FreeMine,
            G1OrcGraveyard,
            G1OrcTempel,
            // G2
            G2NewWorld,
            G2OldWorld,
            G2AddonWorld,
            G2DragonIsland,
        }

        public enum MonsterId
        {
            None				= 0,
            Wolf				= 1,
            BlackWolf			= 2,
            Snapper				= 3,
            OrcBiter			= 4,
            ShadowBeast			= 5,
            Bloodhound			= 6,
            Troll				= 7,
            Waran				= 8,
            FireWaran			= 9,
            Razor				= 10,
            Lurker				= 11,
            SwampShark			= 12,
            Minecrawler			= 13,
            MinecrawlerWarrior	= 14,
            BloodFly			= 15,
            BlackGobbo			= 16,
            Gobbo				= 17,
            Scavenger			= 18,
            Skeleton			= 19,
            SkeletonWarrior		= 20,
            SkeletonScout		= 21,
            SkeletonMage		= 22,
            Demon				= 23,
            DemonLord			= 24,
            MinecrawlerQueen	= 25,
            Molerat 			= 26,
            UndeadOrcWarrior	= 27,
            UndeadOrcShaman		= 28,
            Harpie				= 29,
            Sleeper				= 30,
            StoneGolem			= 31,
            FireGolem			= 32,
            IceGolem			= 33,
            Meatbug				= 34,
            Zombie				= 35
        }
        
        public enum DebugChannel {
            PD_TA_FRAME = 1,
            PD_TA_LOOP = 2,
            PD_TA_CHECK = 3,
            PD_TA_DETAIL = 4,

            PD_ZS_FRAME = 6,
            PD_ZS_LOOP = 7,
            PD_ZS_CHECK = 8,
            PD_ZS_DETAIL = 9,

            // MST - Monster
            PD_MST_FRAME = 11,
            PD_MST_LOOP = 12,
            PD_MST_CHECK = 13,
            PD_MST_DETAIL = 14,

            PD_ORC_FRAME = 16,
            PD_ORC_LOOP = 17,
            PD_ORC_CHECK = 18,
            PD_ORC_DETAIL = 19,

            PD_MISSION = 21,
            PD_CUTSCENE = 22,
            PD_SPELL = 23,
            PD_ITEM_MOBSI = 24,
            PD_MAGIC = 25
        }
    
        [NonSerialized]
        public static Dictionary<WorldToSpawn, string> WorldMappings = new()
        {
            { WorldToSpawn.None, "NO MAPPING AVAILABLE. LOAD WORLD AS STATED IN NEW GAME/SAVE GAME!" },
            // G1
            { WorldToSpawn.G1World, "world.zen" },
            { WorldToSpawn.G1OldMine, "oldmine.zen" },
            { WorldToSpawn.G1FreeMine, "freemine.zen" },
            { WorldToSpawn.G1OrcGraveyard, "orcgraveyard.zen" },
            { WorldToSpawn.G1OrcTempel, "orctempel.zen" },
            // G2
            { WorldToSpawn.G2NewWorld, "newworld.zen" },
            { WorldToSpawn.G2OldWorld, "oldworld.zen" },
            { WorldToSpawn.G2AddonWorld, "addonworld.zen" },
            { WorldToSpawn.G2DragonIsland, "dragonisland.zen" }
        };

        [NonSerialized]
        public static Dictionary<MonsterId, string> MonsterIdMappings = new()
        {
            { MonsterId.None				, "<<None>>" },
            { MonsterId.Wolf				, "Wolf" },
            { MonsterId.BlackWolf			, "Black Wolf" },
            { MonsterId.Snapper				, "Snapper" },
            { MonsterId.OrcBiter			, "Orc Biter" },
            { MonsterId.ShadowBeast			, "Shadow Beast" },
            { MonsterId.Bloodhound			, "Bloodhound" },
            { MonsterId.Troll				, "Troll" },
            { MonsterId.Waran				, "Waran" },
            { MonsterId.FireWaran			, "Fire Waran" },
            { MonsterId.Razor				, "Razor" },
            { MonsterId.Lurker				, "Lurker" },
            { MonsterId.SwampShark			, "Swamp Shark" },
            { MonsterId.Minecrawler			, "Minecrawler" },
            { MonsterId.MinecrawlerWarrior	, "Minecrawler Warrior" },
            { MonsterId.BloodFly			, "Bloodfly" },
            { MonsterId.BlackGobbo			, "Black Gobbo" },
            { MonsterId.Gobbo				, "Gobbo" },
            { MonsterId.Scavenger			, "Scavenger" },
            { MonsterId.Skeleton			, "Skeleton" },
            { MonsterId.SkeletonWarrior		, "Skeleton Warrior" },
            { MonsterId.SkeletonScout		, "Skeleton Scout" },
            { MonsterId.SkeletonMage		, "Skeleton Mage" },
            { MonsterId.Demon				, "Demon" },
            { MonsterId.DemonLord			, "Demonlord" },
            { MonsterId.MinecrawlerQueen	, "Minecrawler Queen" },
            { MonsterId.Molerat 			, "Molerat" },
            { MonsterId.UndeadOrcWarrior	, "Undead Orc Warrior" },
            { MonsterId.UndeadOrcShaman		, "Undead Orc Shaman" },
            { MonsterId.Harpie				, "Harpie" },
            { MonsterId.Sleeper				, "Sleeper" },
            { MonsterId.StoneGolem			, "Stone Golem" },
            { MonsterId.FireGolem			, "Fire Golem" },
            { MonsterId.IceGolem			, "Ice Golem" },
            { MonsterId.Meatbug				, "Meatbug" },
            { MonsterId.Zombie				, "Zombie" },
        };
    }
}
