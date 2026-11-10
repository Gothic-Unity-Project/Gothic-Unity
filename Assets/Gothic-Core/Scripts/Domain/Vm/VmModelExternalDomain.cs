using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Gothic.Core.Domain.Npc.Actions;
using Gothic.Core.Domain.Npc.Actions.AnimationActions;
using Gothic.Core.Const;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.UI;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Context;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Vobs;
using Gothic.Core.Services.World;
using Gothic.Core.Models.Doc;
using Gothic.Core.Creator;
using MyBox;
using Reflex.Attributes;
using ZenKit;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;
using Random = UnityEngine.Random;
using Time = UnityEngine.Time;

namespace Gothic.Core.Domain.Vm
{
    /// <summary>
    /// Daedalus Mdl_* externals (visuals, overlays, scale, face animations).
    /// </summary>
    public class VmModelExternalDomain
    {
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly NpcRandomAniService _npcRandomAniService;
        [Inject] private readonly NpcLookService _npcLookService;
        [Inject] private readonly GameStateService _gameStateService;

        private readonly VmExternalSupport _support;

        private bool _isLoggedOnceRemoveOverlayMds;

        public VmModelExternalDomain(VmExternalSupport support)
        {
            _support = support;
            this.Inject();
        }

        public void Register(VmExternalGuard vm)
        {
            vm.RegisterExternal<NpcInstance, string>("Mdl_SetVisual", Mdl_SetVisual);
            vm.RegisterExternal<NpcInstance, string>("Mdl_ApplyOverlayMds", Mdl_ApplyOverlayMds);
            if (vm.GetSymbolByName("Mdl_ApplyRandomAni") != null)
                vm.RegisterExternal<NpcInstance, string, string>("Mdl_ApplyRandomAni", Mdl_ApplyRandomAni);
            if (vm.GetSymbolByName("Mdl_ApplyRandomAniFreq") != null)
                vm.RegisterExternal<NpcInstance, string, float>("Mdl_ApplyRandomAniFreq", Mdl_ApplyRandomAniFreq);
            vm.RegisterExternal<NpcInstance, string, int, int, string, int, int, int>("Mdl_SetVisualBody",
                Mdl_SetVisualBody);
            vm.RegisterExternal<NpcInstance, float, float, float>("Mdl_SetModelScale", Mdl_SetModelScale);
            vm.RegisterExternal<NpcInstance, float>("Mdl_SetModelFatness", Mdl_SetModelFatness);
            vm.RegisterExternal<NpcInstance, string>("Mdl_RemoveOverlayMDS", Mdl_RemoveOverlayMDS);
            if (vm.GetSymbolByName("Mdl_StartFaceAni") != null)
                vm.RegisterExternal<NpcInstance, string, float, float>("Mdl_StartFaceAni", Mdl_StartFaceAni);
            if (vm.GetSymbolByName("Mdl_ApplyRandomFaceAni") != null)
                vm.RegisterExternal<NpcInstance, string, float, float, float, float, float>("Mdl_ApplyRandomFaceAni",
                    Mdl_ApplyRandomFaceAni);
            // Speed potions (Potions.d: HUMANS_SPRINT.MDS for Time_Haste ms). Declared VAR FLOAT, but the scripts pass
            // INT constants (Time_Haste1) - the stack holds an int, popping a float fails.
            if (vm.GetSymbolByName("Mdl_ApplyOverlayMDSTimed") != null)
                vm.RegisterExternal<NpcInstance, string, int>("Mdl_ApplyOverlayMDSTimed", Mdl_ApplyOverlayMDSTimed);
        }

        public void Mdl_SetVisual(NpcInstance npc, string visual)
        {
            _npcService.ExtMdlSetVisual(npc, visual);
        }

        /// <summary>
        /// While the state animation loops, the random animation plays now and then (NpcRandomAniService).
        /// </summary>
        public void Mdl_ApplyRandomAni(NpcInstance npc, string stateAnimation, string randomAnimation)
        {
            _npcRandomAniService.AddRandomAni(npc, stateAnimation, randomAnimation);
        }

        /// <param name="seconds">Average time between the random animations.</param>
        public void Mdl_ApplyRandomAniFreq(NpcInstance npc, string stateAnimation, float seconds)
        {
            _npcRandomAniService.SetRandomAniFrequency(npc, stateAnimation, seconds);
        }

        public void Mdl_ApplyOverlayMds(NpcInstance npc, string overlayName)
        {
            _npcService.ExtApplyOverlayMds(npc, overlayName);
        }

        public void Mdl_SetVisualBody(NpcInstance npc, string body, int bodyTexNr, int bodyTexColor, string head,
            int headTexNr, int teethTexNr, int armor)
        {
            _npcService.ExtSetVisualBody(new ExtSetVisualBodyData
                {
                    Npc = npc,
                    Body = body,
                    BodyTexNr = bodyTexNr,
                    BodyTexColor = bodyTexColor,
                    Head = head,
                    HeadTexNr = headTexNr,
                    TeethTexNr = teethTexNr,
                    Armor = armor
                }
            );
        }

        public void Mdl_SetModelScale(NpcInstance npc, float x, float y, float z)
        {
            _npcService.ExtMdlSetModelScale(npc, new Vector3(x, y, z));
        }

        public void Mdl_SetModelFatness(NpcInstance npc, float fatness)
        {
            _npcService.ExtSetModelFatness(npc, fatness);
        }

        public void Mdl_RemoveOverlayMDS(NpcInstance npc, string overlayName)
        {
            if (!_isLoggedOnceRemoveOverlayMds)
                Logger.LogWarning("Method >Mdl_RemoveOverlayMDS< not yet implemented in DaedalusVM. Logging once only!",
                    LogCat.ZenKit);
            _isLoggedOnceRemoveOverlayMds = true;
        }

        public void Mdl_StartFaceAni(NpcInstance npc, string name, float intensity, float holdTime)
        {
            _npcLookService.ExtMdlStartFaceAni(npc, name, intensity, holdTime);
        }

        public void Mdl_ApplyRandomFaceAni(NpcInstance npc, string name, float timeMin, float timeMinVar, float timeMax,
            float timeMaxVar, float probMin)
        {
            _npcLookService.ExtMdlApplyRandomFaceAni(npc, name, timeMin, timeMinVar, timeMax, timeMaxVar, probMin);
        }

        /// <summary>
        /// The VR hero has no animations - a timed overlay (sprint potion) speeds up the VR movement instead. NPCs get
        /// the
        /// overlay like Mdl_ApplyOverlayMds (FIXME: not removed after the time yet - Mdl_RemoveOverlayMDS is a stub).
        /// </summary>
        public void Mdl_ApplyOverlayMDSTimed(NpcInstance npc, string overlayName, int timeTicks)
        {
            var seconds = timeTicks / 1000f;
            if (npc != null && npc.Index == ((NpcInstance)_gameStateService.GothicVm.GlobalHero)?.Index)
            {
                Logger.Log($"[Mdl_ApplyOverlayMDSTimed] Hero: {overlayName} for {seconds:F0} s", LogCat.Npc);
                GlobalEventDispatcher.ScriptHeroOverlayTimed.Invoke(overlayName, seconds);
                return;
            }

            Mdl_ApplyOverlayMds(npc, overlayName);
        }
    }
}
