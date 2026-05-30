using System;

namespace GUZ.Core.Models.Vm
{
    public static class VmGothicEnums
    {
        public enum FightMode
        {
            None  = 0,
            Fists = 1,
            Melee = 2,
            Far   = 5,
            Magic = 7,
        }

        public enum PerceptionType
        {
            AssessPlayer = 1,
            AssessEnemy = 2,
            AssessFighter = 3,
            AssessBody = 4,
            AssessItem = 5,
            AssessMurder = 6,
            AssessDefeat = 7,
            AssessDamage = 8,
            AssessOthersDamage = 9,
            AssessThreat = 10,
            AssessRemoveWeapon = 11,
            ObserveIntruder = 12,
            AssessFightSound = 13,
            AssessQuietSound = 14,
            AssessWarn = 15,
            CatchThief = 16,
            AssessTheft = 17,
            AssessCall = 18,
            AssessTalk = 19,
            AssessGivenItem = 20,
            AssessFakeGuild = 21,
            MoveMob = 22,
            MoveNpc = 23,
            DrawWeapon = 24,
            ObserveSuspect = 25,
            NpcCommand = 26,
            AssessMagic = 27,
            AssesssTopMagic = 28,
            AssessCaster = 29,
            AssessSurprise = 30,
            AssessEnterRoom = 31,
            AssessUseMob = 32
        }
        
        public enum AnimationType
        {
            NoAnim,
            Idle,
            Move,

            MoveBack,
            MoveL,
            MoveR,
            RotL,
            RotR,
            WhirlL,
            WhirlR,
            Fall,
            FallDeep,
            FallDeepA,
            FallDeepB,

            Jump,
            JumpUpLow,
            JumpUpMid,
            JumpUp,
            JumpHang,
            Fallen,
            FallenA,
            FallenB,
            SlideA,
            SlideB,

            DeadA,
            DeadB,
            UnconsciousA,
            UnconsciousB,

            InteractIn,
            InteractOut,
            InteractToStand,
            InteractFromStand,

            Attack,
            AttackL,
            AttackR,
            AttackBlock,
            AttackFinish,
            StumbleA,
            StumbleB,
            AimBow,
            PointAt,

            ItmGet,
            ItmDrop,

            MagNoMana
        };

        public enum Attitude
        {
            Null     = -1, // Unset by default
            Hostile  = 0,
            Angry    = 1,
            Neutral  = 2,
            Friendly = 3
        }

        public enum WalkMode
        {
            Run = 0,
            Walk = 1,
            Sneak = 2,
            Water = 4,
            Swim = 8,
            Dive = 16
        }

        public enum Talent
        {
            Unknown = 0,
            _1H = 1,
            _2H = 2,
            Bow = 3,
            Crossbow = 4,
            Picklock = 5,
            Mage = 7,
            Sneak = 8,
            Regenerate = 9,
            Firemaster = 10,
            Acrobat = 11,
            Pickpocket = 12,
            Smith = 13,
            Runes = 14,
            Alchemy = 15,
            Takeanimaltrophy = 16,
            Foreignlanguage = 17,
            Wispdetector = 18,
            C = 19,
            D = 20,
            E = 21,

            MaxG1 = 12,
            MaxG2 = 22
        }

        [Flags]
        public enum BodyState
        {
            // Interruptable Flags
            BsFlagInterruptable = 32768,
            BsFlagFreehands = 65536,

            // ******************************************
            // BodyStates / Overlays and Flags
            // ******************************************
            BsStand = 0 | BsFlagInterruptable | BsFlagFreehands,
            BsWalk = 1 | BsFlagInterruptable, // PointAt not possible
            BsSneak = 2 | BsFlagInterruptable,
            BsRun = 3, // PointAt not possible
            BsSprint = 4, // PointAt not possible
            BsSwim = 5,
            BsCrawl = 6,
            BsDive = 7,
            BsJump = 8,
            BsClimb = 9 | BsFlagInterruptable, // GE�NDERT!
            BsFall = 10,
            BsSit = 11 | BsFlagFreehands,
            BsLie = 12,
            BsInventory = 13,
            BsIteminteract = 14 | BsFlagInterruptable,
            BsMobinteract = 15,
            BsMobinteractInterrupt = 16 | BsFlagInterruptable,

            BsTakeitem = 17,
            BsDropitem = 18,
            BsThrowitem = 19,
            BsPickpocket = 20 | BsFlagInterruptable,

            BsStumble = 21,
            BsUnconscious = 22,
            BsDead = 23,

            BsAimnear = 24, // wird z.Zt nicht benutzt
            BsAimfar = 25, // d.h. Bogenschütze kann weiterschießen, auch wenn er geschlagen wird
            BsHit = 26 | BsFlagInterruptable,
            BsParade = 27,

            // Magic
            BsCasting = 28 | BsFlagInterruptable,
            BsPetrified = 29,
            BsControlling = 30 | BsFlagInterruptable,

            BsMax = 31,

            // Modifier / Overlays
            BsModHidden = 128,
            BsModDrunk = 256,
            BsModNuts = 512,
            BsModBurning = 1024,
            BsModControlled = 2048,
            BsModTransformed = 4096
        }

        [Flags]
        public enum ItemFlags
        {
            // Item categories
            ItemKatNone = 1 << 0, // misc
            ItemKatNf = 1 << 1, // melee weapons
            ItemKatFf = 1 << 2, // distant-combat weapons
            ItemKatMun = 1 << 3, // munition (->MultiSlot)
            ItemKatArmor = 1 << 4, // armor and helmets
            ItemKatFood = 1 << 5, // food (->MultiSlot)
            ItemKatDocs = 1 << 6, // documents
            ItemKatPotions = 1 << 7, // potions
            ItemKatLight = 1 << 8, // light sources
            ItemKatRune = 1 << 9, // runes and scrolls
            ItemKatMagic = 1 << 31, // rings and amulets
            ItemKatKeys = ItemKatNone,

            // Item flags
            ItemBurn = 1 << 10, // can be burnt (i.e. torch)
            ItemMission = 1 << 12, // mission item
            ItemMulti = 1 << 21, // is multi
            ItemTorch = 1 << 28, // use like a torch
            ItemThrow = 1 << 29, // item can be thrown

            // Item weapon flags
            ItemSwd = 1 << 14, // use like sword
            ItemAxe = 1 << 15, // use like axe
            Item2HdSwd = 1 << 16, // use like two handed weapon
            Item2HdAxe = 1 << 17, // use like two handed axe
            ItemBow = 1 << 19, // use like bow
            ItemCrossbow = 1 << 20, // use like crossbow
            ItemAmulet = 1 << 22, // use like amulet
            ItemRing = 1 << 11 // use like ring
        }

        public enum InvCats
        {
            InvWeapon = 1,
            InvArmor  = 2,
            InvRune   = 3,
            InvMagic  = 4,
            InvFood	  = 5,
            InvPotion = 6,
            InvDoc    = 7,
            InvMisc   = 8,
            InvCatMax = 9
        }

        public static InvCats ToInventoryCategory(this ItemFlags mainFlag)
        {
            switch (mainFlag) {
                case ItemFlags.ItemKatNf:
                case ItemFlags.ItemKatFf:
                case ItemFlags.ItemKatMun:
                    return InvCats.InvWeapon;
                case ItemFlags.ItemKatArmor:
                    return InvCats.InvArmor;
                case ItemFlags.ItemKatFood:
                    return InvCats.InvFood;
                case ItemFlags.ItemKatDocs:
                    return InvCats.InvDoc;
                case ItemFlags.ItemKatPotions:
                    return InvCats.InvPotion;
                case ItemFlags.ItemKatRune:
                    return InvCats.InvRune;
                case ItemFlags.ItemKatMagic:
                    return InvCats.InvMagic;
                // None and others
                default:
                    return InvCats.InvMisc;
            }
        }

        /// <summary>
        /// Used for INpc.FightMode
        /// </summary>
        public enum WeaponState
        {
            NoWeapon,
            Fist,
            W1H,
            W2H,
            Bow,
            CBow,
            Mage
        }

        public enum MoverState
        {
            Open,
            Opening,
            Closed,
            Closing
        }

        /// <summary>
        /// Seems like it's used inside Gothic to define if an object is "active" (Awake) or rendered only (DoAiOnly)
        /// </summary>
        public enum VobSleepMode
        {
            Sleeping = 0,
            Awake = 1,
            AwakeDoAiOnly = 2
        }

        public enum ItemMaterial
        {
            Wood    = 0,
            Stone   = 1,
            Metal   = 2,
            Leather = 3,
            Clay    = 4,
            Glass   = 5,
            Cound   = 6,
        }

        // FIXME - These are G1 guilds. Once we implement G2, we need to outsource this enum to their respective Unity modules for G1/G2.
        public enum Guild
        {
            //																				 
            //	HUMAN GUILDS																 
            //	
            GIL_NONE          =  0,		//	gildenlose
            GIL_HUMAN         =  1, //	Special Guild -> Set Constants for all Human Guilds
            GIL_EBR           =  1, //	Erzbarone 6* +3Babes
            GIL_GRD           =  2, //	Guard
            GIL_STT           =  3, //	Shadow
            GIL_KDF           =  4, //	Ring of fire (Kreis des Feuers)
            GIL_VLK           =  5, //	Buddler (Volk)
            GIL_KDW           =  6, //	Ring of water (Kreis des Wassers)
            GIL_SLD           =  7, //	Soldier
            GIL_ORG           =  8, //	Organisators
            GIL_BAU           =  9, //	Farmer (Bauer)
            GIL_SFB           = 10, //	Prospector (Schürferbund)
            GIL_GUR           = 11, //	Gurus
            GIL_NOV           = 12, //	Novices
            GIL_TPL           = 13, //	Templer
            GIL_DMB           = 14, //	Demon magician (Dämonenbeschwörer)
            GIL_BAB           = 15, //	Babe
            GIL_SEPERATOR_HUM = 16,
            MAX_GUILDS        = 16,
            
            //																				 
            //	MONSTER GUILDS																 
            //																				 
            GIL_WARAN         = 17,
            GIL_SLF           = 18, //	Sleeper
            GIL_GOBBO         = 19,
            GIL_TROLL         = 20,
            GIL_SNAPPER       = 21,
            GIL_MINECRAWLER   = 22, //	Minecrawler & Queen
            GIL_MEATBUG       = 23,
            GIL_SCAVENGER     = 24,
            GIL_DEMON         = 25,
            GIL_WOLF          = 26,
            GIL_SHADOWBEAST   = 27,
            GIL_BLOODFLY      = 28,
            GIL_SWAMPSHARK    = 29,
            GIL_ZOMBIE        = 30,
            GIL_UNDEADORC     = 31, //	Undead Orcs (Warrier & Priest)
            GIL_SKELETON      = 32,
            GIL_ORCDOG        = 33,
            GIL_MOLERAT       = 34,
            GIL_GOLEM         = 35,
            GIL_LURKER        = 36,
            GIL_SEPERATOR_ORC = 37,
            GIL_ORCSHAMAN     = 38,
            GIL_ORCWARRIOR    = 39,
            GIL_ORCSCOUT      = 40,
            GIL_ORCSLAVE      = 41,
            GIL_MAX           = 42
        }
    }
}
