using System;
using GUZ.Core.Models.Caches;

namespace GUZ.Core.Extensions
{
    public static class PrefabTypeExtension
    {
        public static string Path(this PrefabType type)
        {
            return type switch
            {
                PrefabType.UiEmpty => "Prefabs/UI/Empty",
                PrefabType.UiText => "Prefabs/UI/Text",
                PrefabType.UiButton => "Prefabs/UI/Button",
                PrefabType.UiSlider => "Prefabs/UI/Slider",
                PrefabType.UiButtonTextured => "Prefabs/UI/ButtonTextured",
                PrefabType.UiTexture => "Prefabs/UI/Texture",
                PrefabType.UiThumbnail => "Prefabs/UI/Thumbnail",
                
                PrefabType.UiDebugText => "Prefabs/UI/Debug/Text",
                PrefabType.UiDebugLogLine => "Prefabs/UI/Debug/LogLine",
                PrefabType.UiDebugButton => "Prefabs/UI/Debug/Button",
                PrefabType.UiDebugToggleButton => "Prefabs/UI/Debug/ToggleButton",
                PrefabType.UiDebugToggle => "Prefabs/UI/Debug/Toggle",
                PrefabType.UiDebugSlider => "Prefabs/UI/Debug/Slider",

                PrefabType.WayPoint => "Prefabs/WayPoint",
                PrefabType.Vob => "Prefabs/Vobs/Vob",
                PrefabType.Npc => "Prefabs/Vobs/oCNpc",
                PrefabType.VobAnimate => "Prefabs/Vobs/zCVobAnimate",
                PrefabType.VobContainer => "Prefabs/Vobs/oCMobContainer",
                PrefabType.VobDoor => "Prefabs/Vobs/oCMobDoor",
                PrefabType.VobFire => "Prefabs/Vobs/oCMobFire",
                PrefabType.VobBed => "Prefabs/Vobs/oCMobBed",
                PrefabType.VobWheel => "Prefabs/Vobs/oCMobWheel",
                PrefabType.VobSwitch => "Prefabs/Vobs/oCMobSwitch",
                PrefabType.VobInteractable => "Prefabs/Vobs/oCMobInter",
                PrefabType.VobInteractableSeat => "Prefabs/Vobs/oCMobInterSeat",
                PrefabType.VobMovable => "Prefabs/Vobs/oCMobMovable",
                PrefabType.VobSpot => "Prefabs/Vobs/zCVobSpot",
                PrefabType.VobPfx => "Prefabs/Vobs/vobPfx",
                PrefabType.VobMusic => "Prefabs/Vobs/oCZoneMusic",
                PrefabType.VobSound => "Prefabs/Vobs/zCVobSound",
                PrefabType.VobSoundDaytime => "Prefabs/Vobs/zCVobSoundDaytime",
                PrefabType.VobLadder => "Prefabs/Vobs/oCMobLadder",
                PrefabType.VobLight => "Prefabs/Vobs/zCVobLight",
                PrefabType.VobMover => "Prefabs/Vobs/zCMover",
                PrefabType.VobTriggerChangeLevel => "Prefabs/Vobs/oCTriggerChangeLevel",
                PrefabType.VobTriggerScript => "Prefabs/Vobs/oCTriggerScript",
                
                PrefabType.VobItem => "Prefabs/Vobs/oCItem",
                PrefabType.VobItemLockPick => "Prefabs/Vobs/oCItem/LockPick",
                PrefabType.VobItemWeapon => "Prefabs/Vobs/oCItem/Weapon",
                
                PrefabType.StoryIntroduceChapter => "Prefabs/Story/IntroduceChapter",
                _ => throw new Exception($"Enum value {type} not yet defined.")
            };
        }
    }
}
