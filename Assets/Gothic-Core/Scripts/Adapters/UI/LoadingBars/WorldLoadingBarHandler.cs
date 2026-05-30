using System;
using System.Collections.Generic;
using System.Linq;
using GUZ.Core.Services.World;
using MyBox;
using Reflex.Attributes;

namespace GUZ.Core.Adapters.UI.LoadingBars
{
    public class WorldLoadingBarHandler : AbstractLoadingBarHandler
    {
        [Inject] private readonly SaveGameService _saveGameService;
        
        public enum ProgressType
        {
            WorldMesh,
            VOB,
            Npc
        }
        
        public override List<string> GetProgressTypes()
        {
            return Enum.GetNames(typeof(ProgressType)).ToList();
        }

        protected override void SetMaterials()
        {
            base.SetMaterials();
            
            // On G1+world.zen it's either Gomez in his throne room (NewGame) or Gorn holding his Axe (LoadGame)
            var textureNameForLoadingScreen = _saveGameService.IsNewGame
                ? "LOADING.TGA"
                : $"LOADING_{_saveGameService.CurrentWorldName.ToUpper().RemoveEnd(".ZEN")}.TGA";
            TextureService.SetTexture(textureNameForLoadingScreen, TextureService.GothicLoadingMenuMaterial);
        }
    }
}
