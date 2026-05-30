using GUZ.Core;
using GUZ.Core.Models.Caches;
using GUZ.Core.Services;
using GUZ.Core.Services.Caches;
using UnityEngine;

namespace GUZ.Lab.Handler
{
    public class LabLadderLabHandler : AbstractLabHandler
    {
        public GameObject LadderSlot;

        public override void Bootstrap()
        {
            var itemPrefab = ResourceCacheService.TryGetPrefabObject(PrefabType.VobLadder);
            var ladderName = "LADDER_3.MDL";
            var mdl = ResourceCacheService.TryGetModel(ladderName);

            MeshService.CreateVob(ladderName, mdl, Vector3.zero, Quaternion.Euler(0, 180, 0), LadderSlot, itemPrefab, false);
        }
    }
}
