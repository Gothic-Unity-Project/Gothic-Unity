using System;
using GUZ.Core.Extensions;
using GUZ.Core.Services;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;

namespace GUZ.Core.Domain.Vm
{
    public class VmIkarusLeGoDomain
    {
        [Inject] private readonly GameStateService _gameStateService;
        
        
        public VmIkarusLeGoDomain()
        {
            this.Inject();
        }
        
        public void Init()
        {
            // Core
            _gameStateService.GothicVm.OverrideFunction("MEMINT_SetupExceptionHandler", () => {}); // NOP
            _gameStateService.GothicVm.OverrideFunction<int, string>("MEMINT_HandleError", MEMINT_HandleError);
            _gameStateService.GothicVm.OverrideFunction("LOCALS", Locals);
            _gameStateService.GothicVm.OverrideFunction("MEMINT_SetupExceptionHandler", SetupExceptionHandler);

            
            
            
            // Trialoge
            _gameStateService.GothicVm.OverrideFunction<NpcInstance>("TRIA_Invite", TRIA_Invite);
            _gameStateService.GothicVm.OverrideFunction("TRIA_Start", TRIA_Start);
            _gameStateService.GothicVm.OverrideFunction<NpcInstance>("TRIA_Next", TRIA_Next);
        }

        private void SetupExceptionHandler()
        {
            throw new System.NotImplementedException();
        }

        private void Locals()
        {
            throw new System.NotImplementedException();
        }

        public void MEMINT_HandleError(int errorType, string text)
        {
            
        }
        
        public void TRIA_Invite(NpcInstance npc)
        {
            Debug.LogWarning("TRIA_Invite");
        }

        public void TRIA_Start()
        {
            Debug.LogWarning("TRIA_Start");
        }

        public void TRIA_Next(NpcInstance npc)
        {
            Debug.LogWarning("TRIA_Next");
        }
    }
}
