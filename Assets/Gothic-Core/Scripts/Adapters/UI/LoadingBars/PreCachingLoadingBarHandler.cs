using System;
using System.Collections.Generic;

namespace GUZ.Core.Adapters.UI.LoadingBars
{
    public class PreCachingLoadingBarHandler : AbstractLoadingBarHandler
    {
        [NonSerialized]
        public int LevelCount;
        
        
        public enum ProgressTypesPerWorld
        {
            CalculateVobBounds,
            CalculateTextureArrayInformationMesh,
            CalculateTextureArrayInformationVobs,
            CalculateStationaryLights,
            CalculateWorldChunks
        }

        public enum ProgressTypesGlobal
        {
            CalculateItemTextureArrayInformation,
            CalculateVobItemBounds,
            CalculateVobItemCollider
        }
        
        public override List<string> GetProgressTypes()
        {
            var returnValue = new List<string>();

            foreach (var worldType in Enum.GetValues(typeof(ProgressTypesPerWorld)))
            {
                for (var i = 0; i < LevelCount; i++)
                {
                    returnValue.Add($"{worldType}_{i}");
                }
            }

            foreach (var globalType in Enum.GetValues(typeof(ProgressTypesGlobal)))
            {
                returnValue.Add(globalType.ToString());
            }
            
            return returnValue;
        }
    }
}
