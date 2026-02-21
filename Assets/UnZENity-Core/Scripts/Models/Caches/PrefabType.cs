using System;
using GUZ.Core.Models.Caches;

namespace GUZ.Core.Models.Caches
{
    public enum PrefabType
    {
        UiEmpty, // Contains a RectTransform only
        UiText,
        UiButton,
        UiSlider,
        UiButtonTextured,
        UiTexture,
        UiThumbnail,
        
        UiDebugText,
        UiDebugLogLine,
        UiDebugButton,
        UiDebugToggleButton,
        UiDebugToggle,
        UiDebugSlider,

        WayPoint,
        Vob,
        Npc,
        VobAnimate,
        VobContainer,
        VobDoor,
        VobFire,
        VobBed,
        VobWheel,
        VobSwitch,
        VobInteractable,
        VobInteractableSeat,
        VobMovable,
        VobSpot,
        VobPfx,
        VobMusic,
        VobSound,
        VobSoundDaytime,
        VobLadder,
        VobLight,
        VobMover,
        VobTriggerChangeLevel,
        VobTriggerScript,

        VobItem,
        VobItemLockPick,
        VobItemWeapon,
        
        StoryIntroduceChapter
    }
}
