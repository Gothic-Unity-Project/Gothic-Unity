using GUZ.Core.Extensions;
using GUZ.Core.Services.World;
using Reflex.Attributes;
using UnityEngine;

namespace GUZ.Core.Debugging
{
    public class WorldMeshDebug : MonoBehaviour
    {
        // Examples: G1.World -> 2090...2100 Old camp tower
        [SerializeField] private bool _showLeafNodes;
        // -1 means all of them.
        [SerializeField] private int _leafNodeStart = -1;
        [SerializeField] private int _leafNodeCount = -1;

        [SerializeField] private int _showSectionsAmount = -1;

        [SerializeField] private int _showPortalsAmount = -1;

        [Inject] private SaveGameService _saveGameService;

        
        private void OnDrawGizmos()
        {
            if (!_showLeafNodes)
                return;

            if (_saveGameService?.CurrentWorldData?.BspTree == null)
                return;

            var bspTree = _saveGameService.CurrentWorldData.BspTree;

            if (_showLeafNodes)
            {
                var leafNodes = bspTree.LeafNodeIndices;
                
                Gizmos.color = new Color(0, 1, 0, 0.3f);

                var startIndex = Mathf.Max(0, _leafNodeStart);
                for (var i = Mathf.Max(0, _leafNodeStart); i < (_leafNodeCount <= -1 ? leafNodes.Count : startIndex + _leafNodeCount); i++)
                {
                    // Safety net.
                    if (i >= leafNodes.Count)
                        break;
                    
                    var leafNode = bspTree.GetNode(leafNodes[i]);
                    var bbox = leafNode.BoundingBox;
                    var center = (bbox.Min.ToUnityVector() + bbox.Max.ToUnityVector()) / 2f;
                    var size = bbox.Max.ToUnityVector() - bbox.Min.ToUnityVector();

                    Gizmos.DrawWireCube(center, size);
                }
            }
            
            // if (_showSectionsAmount != 0)
            // {
            //     var sections = bspTree.Sectors;
            //     var maxSections = _showSectionsAmount < 0 ? sections.Count : Mathf.Min((int)_showSectionsAmount, sections.Count);
            //
            //     Gizmos.color = new Color(0, 0, 1, 0.3f);
            //
            //     for (var i = 0; i < maxSections; i++)
            //     {
            //         var section = sections[i];
            //         var bbox = section.BoundingBox;
            //         var center = (bbox.Min.ToUnityVector() + bbox.Max.ToUnityVector()) / 2f;
            //         var size = bbox.Max.ToUnityVector() - bbox.Min.ToUnityVector();
            //
            //         Gizmos.DrawWireCube(center, size);
            //     }
            // }
            //
            // if (_showPortalsAmount != 0)
            // {
            //     var portals = bspTree.PortalPolygonIndices;
            //     var maxPortals = _showPortalsAmount < 0 ? portals.Count : Mathf.Min((int)_showPortalsAmount, portals.Count);
            //
            //     Gizmos.color = new Color(1, 0, 0, 0.3f);
            //
            //     for (var i = 0; i < maxPortals; i++)
            //     {
            //         var portal = bspTree.GetPolygon(portals[i]);
            //         var bbox = portal.BoundingBox;
            //         var center = (bbox.Min.ToUnityVector() + bbox.Max.ToUnityVector()) / 2f;
            //         var size = bbox.Max.ToUnityVector() - bbox.Min.ToUnityVector();
            //
            //         Gizmos.DrawWireCube(center, size);
            //     }
            // }
        }
    }
}
