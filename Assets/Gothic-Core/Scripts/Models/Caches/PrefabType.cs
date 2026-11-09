using System;
using Gothic.Core.Models.Caches;

namespace Gothic.Core.Models.Caches
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
        UiVirtualKeyboard,

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
        VobInteractableDigSpot,
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
        VobTrigger,
        VobTriggerList,
        VobTriggerUntouch,
        VobTouchDamage,
        VobTriggerWorldStart,
        VobCodeMaster,
        VobMoverController,
        VobMessageFilter,

        VobItem,
        VobItemLockPick,
        VobItemWeapon,

        StoryIntroduceChapter
    }
}
