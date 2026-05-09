using GUZ.Core.Extensions;
using GUZ.Core.Services;
using UnityEngine;
using ZenKit.Vobs;
using Material = UnityEngine.Material;

namespace GUZ.Core.Const
{
    public static class Constants
    {
        public static class Animations
        {
            public const string RootBoneName = "BIP01";
        }

        // Unity shaders
        public static readonly Shader ShaderUnlit = Shader.Find("Universal Render Pipeline/Unlit");
        public static readonly Shader ShaderUnlitParticles = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        public static readonly Shader ShaderTMPSprite = Shader.Find("TextMeshPro/Sprite");
        public static readonly Shader ShaderDecal = Shader.Find("Shader Graphs/Decal");
        public static readonly Shader ShaderStandard = Shader.Find("Standard");

        public static readonly Material LoadingMaterial; // Used for Vobs and World before applying TextureArray.
        public static readonly Material DebugMaterial = new(ShaderUnlit); // Used for Marvin mode elements like visible WayPoints.

        // Custom shaders
        // For textures like NPCs, _not_ the grouped texture array.
        public static readonly string ShaderSingleMeshLitName = "Lit/SingleMesh";
        public static readonly Shader ShaderSingleMeshLit = Shader.Find(ShaderSingleMeshLitName);
        public static readonly Shader ShaderSingleMeshLitDynamic = Shader.Find("Lit/SingleMesh-Dynamic");

        public static readonly string ShaderWorldLitName = "Lit/World";
        public static readonly Shader ShaderWorldLit = Shader.Find(ShaderWorldLitName);

        public static readonly string ShaderLitAlphaToCoverageName = "Lit/AlphaToCoverage";
        public static readonly Shader ShaderLitAlphaToCoverage = Shader.Find(ShaderLitAlphaToCoverageName);
        public static readonly Shader ShaderLitAlphaToCoverageDynamic = Shader.Find("Lit/AlphaToCoverage-Dynamic");
        
        public static readonly Shader ShaderWater = Shader.Find("Lit/Water");
        public static readonly Shader ShaderBarrier = Shader.Find("Unlit/Barrier");
        public static readonly Shader ShaderThunder = Shader.Find("Unlit/ThunderShader");

        public static readonly Texture2D TextureUnZENityLogo = Resources.Load<Texture2D>("Gothic-UnZENity-logo");
        public static readonly Texture2D TextureUnZENityLogoTransparent = Resources.Load<Texture2D>("Gothic-UnZENity-logo-transparent");
        public static readonly Texture2D TextureUnZENityLogoInverse = Resources.Load<Texture2D>("Gothic-UnZENity-logo-inverse");
        public static readonly Texture2D TextureUnZENityLogoInverseTransparent = Resources.Load<Texture2D>("Gothic-UnZENity-logo-inverse-transparent");


        /*
         * Shader properties
         */

        public static readonly int ShaderTypeDefault = 0;
        public static readonly int ShaderTypeTransparent = 3000;

        // Interactable VOB is in focus of player. Brighten up the color by this value.
        public static readonly int ShaderPropertyFocusBrightness = Shader.PropertyToID("_FocusBrightness");
        public static readonly float ShaderPropertyFocusBrightnessDefault = 1f;
        public static readonly float ShaderPropertyFocusBrightnessValue = 10f;

        // Item is a "ghost" in hand. i.e. no collision.
        public static readonly int ShaderPropertyTransparency = Shader.PropertyToID("_Alpha");
        public static readonly float ShaderPropertyTransparencyDefault = 1f;
        public static readonly float ShaderPropertyTransparencyValue = 0.60f;

        public const string SceneBootstrap = "Bootstrap";
        public const string ScenePlayer = "Player";
        public const string SceneGameVersion = "GameVersion";
        public const string ScenePreCaching = "PreCaching";
        public const string SceneLogo = "Logo";
        public const string SceneMainMenu = "MainMenu";
        public const string SceneLoading = "Loading";
        public const string SceneDefaultWorld = "DefaultWorld";
        public const string SceneLab = "Lab";

        // Water settings
        public static readonly int ShaderPropertyWaterEffectToggle = Shader.PropertyToID("_UnderwaterEffect");
        

        // Hint: We will never be able to cache lights as their radius is always dynamic.
        public static readonly VirtualObjectType[] StaticCacheVobTypes =
        {
            VirtualObjectType.oCItem,
            VirtualObjectType.oCMOB,
            VirtualObjectType.oCMobBed,
            VirtualObjectType.oCMobContainer,
            VirtualObjectType.oCMobDoor,
            VirtualObjectType.oCMobFire,
            VirtualObjectType.oCMobInter,
            VirtualObjectType.oCMobLadder,
            VirtualObjectType.oCMobSwitch,
            VirtualObjectType.oCMobWheel,
            VirtualObjectType.zCVob,
            VirtualObjectType.zCVobAnimate,
            VirtualObjectType.zCVobStair
        };

        /*
         * ### Layers
         */

        // Unity's built-in layers
        public static LayerMask DefaultLayer = LayerMask.NameToLayer("Default");
        public static LayerMask TransparentFXLayer = LayerMask.NameToLayer("TransparentFX");
        // solves some weird interactions between the teleport raycast and collider (musicZone/worldTriggerChange)
        public static LayerMask IgnoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
        public static LayerMask WaterLayer = LayerMask.NameToLayer("Water");
        public static LayerMask UILayer = LayerMask.NameToLayer("UI");

        // HVR Layers (could also be reused and implemented for XRIT if needed)
        public static LayerMask PlayerLayer = LayerMask.NameToLayer("Player"); // Layer 8 (suggested by HVR)
        public static LayerMask DynamicPoseLayer = LayerMask.NameToLayer("DynamicPose"); // Layer 9 (suggested by HVR)
        public static LayerMask GrabbableLayer = LayerMask.NameToLayer("Grabbable"); // Layer 20 (suggested by HVR)
        public static LayerMask HandLayer = LayerMask.NameToLayer("Hand"); // Layer 21 (suggested by HVR)

        // Custom layers
        public static LayerMask VobItemLayer = LayerMask.NameToLayer("VobItem");
        public static LayerMask VobItemNoWorldCollision = LayerMask.NameToLayer("VobItemNoWorldCollision"); // No collision with world (e.g. while item is in our hands)
        public static LayerMask VobNpcOrMonsterLayer = LayerMask.NameToLayer("VobNpcOrMonster");
        public static LayerMask VobMovableLayer = LayerMask.NameToLayer("VobMovable"); // No collision with world (e.g. for chest lid and door)
        public static LayerMask VobHitbox = LayerMask.NameToLayer("VobHitbox");

        
        /*
         * ### Tags
         */

        // Default Tags
        public const string PlayerTag = "Player";
        public const string MainCameraTag = "MainCamera";

        // Custom Tags
        public const string VobTag = "GUZVob";

        
        // We need to set the scale so that collision and NPC animation is starting at the right spot.
        public static Vector3 VobZsScale = new(0.1f, 0.1f, 0.1f);

        // e.g. for NPCs to check if they reached a FreePoint already. Value is based on best guess/testing.
        public const float NpcDestinationReachedThreshold = 0.6f;
        public const float NpcRotationSpeed = 500f;
        

        public const string SlotRightHand	= "ZS_RIGHTHAND";
        public const string SlotLeftHand	= "ZS_LEFTHAND";
        public const string SlotSword		= "ZS_SWORD";
        public const string SlotLongsword	= "ZS_LONGSWORD";
        public const string SlotBow			= "ZS_BOW";
        public const string SlotCrossbow	= "ZS_CROSSBOW";
        public const string SlotHelmet		= "ZS_HELMET";
        public const string SlotTorso		= "ZS_TORSO";


        public const string DaedalusHeroInstanceName = "PC_HERO"; // TODO - can be read from .ini file.

        // Alter this value to enforce game to recreate cache during next start.
        public const string StaticCacheVersion = "4";

        /// <summary>
        /// Used during pre-caching to calculate world chunks to merge.
        /// We implemented a logic to overcome 9 lights on a mesh - limitation by URP forward rendering.
        /// If you change this value, you also need to alter it inside Shader: StationaryLighting.hlsl --> MAX_AFFECTING_STATIONARY_LIGHTS
        /// </summary>
        public const int MaxLightsPerWorldChunk = 16;

        /*
         * Colors
         */
        
        public static Color TextNormalColor = new Color(1, 1, 1, 1);
        public static Color TextDisabledColor = new Color(1, 1, 1, 0.4f);
        public static Color TextRedColor = new Color(1, 0, 0, 1f);
        public static Color TextYellowColor = new Color(1, 1, 0, 1);
        
        public static string YesLabel = "Yes";
        public static string NoLabel = "No";

        static Constants()
        {
            LoadingMaterial = new Material(ShaderWorldLit);
        }
    }
}
