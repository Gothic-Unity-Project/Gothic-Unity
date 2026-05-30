using GUZ.Core.Const;
using GUZ.Core.Extensions;
using GUZ.Core.Logging;
using UnityEngine;
using Logger = GUZ.Core.Logging.Logger;

namespace GUZ.Core.Domain.Meshes.Builder
{
    public class VobMeshBuilder : AbstractMeshBuilder
    {
        public override GameObject Build()
        {
            if (Mrm != null)
            {
                BuildViaMrm();
            }
            else if (Mdm != null && Mdh != null)
            {
                BuildViaMdmAndMdh();
            }
            else if (Mmb != null)
            {
                BuildViaMmb();
            }
            else
            {
                Logger.LogWarning($"No suitable data for Vob to be created found >{RootGo.name}<", LogCat.Mesh);
                return null;
            }

            AddZsCollider();

            return RootGo;
        }

        /// <summary>
        /// Add ZenGineSlot collider. i.e. positions where an NPC can sit on a bench.
        /// </summary>
        private void AddZsCollider()
        {
            if (!HasMeshCollider || RootGo == null || RootGo.transform.childCount == 0)
            {
                return;
            }

            var zm = RootGo.transform.GetChild(0);
            for (var i = 0; i < zm.childCount; i++)
            {
                var child = zm.GetChild(i).gameObject;
                if (!child.name.StartsWithIgnoreCase("ZS_"))
                {
                    continue;
                }

                // ZS need to be "invisible" for the Raycast teleporter.
                child.layer = Constants.IgnoreRaycastLayer;
                child.transform.localScale = Constants.VobZsScale;
                // Used for event triggers with NPCs.
                var coll = child.AddComponent<SphereCollider>();
                coll.isTrigger = true;
            }
        }
    }
}
