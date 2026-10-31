using System.Collections;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Services.Meshes;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Npc
{
    /// <summary>
    /// How an NPC's body looks beyond its meshes, like the engine (OpenGothic MdlVisual::setFatness, setNpcEffect):
    /// - Fatness (Mdl_SetModelFatness, -1...2 in the scripts): body, head and armor are pushed out along their normals,
    ///   0.5 cm per unit - a fat merchant, a thin miner.
    /// - Ghosts (C_NPC.flags NPC_FLAG_GHOST: Quarhodron, Rhademes, the fire shadowbeast) are see-through.
    /// Applied when the NPC's body is built and when a script changes the fatness.
    /// </summary>
    public class NpcLookService
    {
        // OpenGothic: fatness * 0.5 cm.
        private const float _fatnessMeters = 0.005f;
        private const int _npcFlagGhost = 4;
        private const float _ghostAlpha = 0.45f;
        private static readonly int _fatnessId = Shader.PropertyToID("_Fatness");
        private static readonly int _alphaId = Shader.PropertyToID("_Alpha");

        [Inject] private readonly UnityMonoService _unityMonoService;
        [Inject] private readonly DynamicMaterialService _dynamicMaterialService;

        public NpcLookService()
        {
            GlobalEventDispatcher.NpcMeshCullingChanged.AddListener((npc, _, isInVisibleRange, _) =>
            {
                if (isInVisibleRange)
                    _unityMonoService.StartCoroutine(ApplyLater(npc));
            });
        }

        /// <summary>
        /// The script changed the fatness: the body follows at once, if it exists.
        /// </summary>
        public void SetFatness(NpcContainer npc, float fatness)
        {
            npc.Vob.ModelFatness = fatness;
            if (npc.Go != null)
                ApplyFatness(npc);
        }

        private IEnumerator ApplyLater(NpcContainer npc)
        {
            // The body is built in the same event.
            yield return null;
            if (npc?.Go == null || npc.Instance == null)
                yield break;

            ApplyFatness(npc);
            if ((npc.Instance.Flags & _npcFlagGhost) != 0)
            {
                _dynamicMaterialService.SetDynamicValue(npc.Go, _alphaId, _ghostAlpha);
                Logger.Log($"[NpcLook] {npc.Instance.GetName(NpcNameSlot.Slot0)} is a ghost.", LogCat.Npc);
            }
        }

        /// <summary>
        /// Body, head and armor - not what the NPC holds (items in its hand slots, ZS_*).
        /// </summary>
        private static void ApplyFatness(NpcContainer npc)
        {
            var fatness = npc.Vob.ModelFatness * _fatnessMeters;
            foreach (var meshRenderer in npc.Go.GetComponentsInChildren<Renderer>(true))
            {
                if (IsHeldItem(meshRenderer.transform, npc.Go.transform))
                    continue;

                foreach (var material in meshRenderer.sharedMaterials)
                {
                    if (material != null && material.HasProperty(_fatnessId))
                        material.SetFloat(_fatnessId, fatness);
                }
            }
        }

        private static bool IsHeldItem(Transform transform, Transform root)
        {
            for (var current = transform; current != null && current != root; current = current.parent)
            {
                if (current.name.StartsWithIgnoreCase("ZS_"))
                    return true;
            }
            return false;
        }
    }
}
