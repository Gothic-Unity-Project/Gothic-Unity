#if GUZ_HVR_INSTALLED
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GUZ.Core;
using GUZ.Core.Adapters.Vob;
using GUZ.Core.Extensions;
using GUZ.Core.Logging;
using GUZ.Core.Models.Caches;
using GUZ.Core.Models.Container;
using GUZ.Core.Services.Caches;
using GUZ.Core.Services.Meshes;
using GUZ.Core.Services.Vobs;
using HurricaneVR.Framework.Components;
using HurricaneVR.Framework.Core;
using HurricaneVR.Framework.Core.Sockets;
using MyBox;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using ZenKit.Vobs;
using Logger = GUZ.Core.Logging.Logger;

namespace GUZ.VR.Adapters.Vob.Container
{
    public class VRVobContainerPhysicsChest : HVRPhysicsDoor
    {
        [Inject] private readonly MeshService _meshService;
        [Inject] private readonly VobService _vobService;
        [Inject] private readonly ResourceCacheService _resourceCacheService;


        private readonly char[] _itemNameSeparators = { ';', ',' };
        private readonly char[] _itemCountSeparators = { ':', '.' };

        // Need to be updated if ocMobContainer.prefab is changed.
        private const int _socketRows = 3;
        private const int _socketsPerRow = 4;
        private const float _socketRadius = 0.1f;
        private const float _socketCollectionMargin = _socketRadius / 2;

        [Separator("GUZ - Settings")]
        [SerializeField] private GameObject _rootGo;
        [SerializeField] private HVRSocketContainer _socketContainer;
        [SerializeField] private BoxCollider _collectorCollider;
        private IContainer _vobContainer;

        // Flags to ensure we always call On*() once, everytime open/close status changes.
        private bool _openingForTheFirstTime = true;
        private bool _onOpenedHandled;
        private bool _onClosedHandled;
        
        
        [Serializable]
        [Obsolete("Moved to GUZ.Core.Data.Vobs.ContentItem")]
        public struct ContentItem
        {
            public string Name;
            public int Amount;
        }
        
        
        public override void Start()
        {
            base.Start();
            
            _socketContainer.gameObject.SetActive(false);
            
            _vobContainer =  _rootGo.GetComponentInParent<VobLoader>().Container.VobAs<IContainer>();
        }
        
        protected override void Update()
        {
            base.Update();

            if (Opened && !_onOpenedHandled)
            {
                OnOpened();
            }

            if (Closed && !_onClosedHandled)
            {
                OnClosed();
            }
        }
        
        private void OnOpened()
        {
            if (_openingForTheFirstTime)
            {
                AlignSockets();
                StartCoroutine(InitializeContent());
                _openingForTheFirstTime = false;
            }
            
            // Render items if opened for the first time.
            _socketContainer.gameObject.SetActive(true);
            
            _onOpenedHandled = true;
            _onClosedHandled = false;
        }
        
        private void OnClosed()
        {
            _socketContainer.gameObject.SetActive(false);
            
            _onOpenedHandled = false;
            _onClosedHandled = true;
        }

        /// <summary>
        /// Containers have different sizes. Re-align the prefab's HVR sockets to match their actual sizes.
        /// </summary>
        private void AlignSockets()
        {
            var sockets = _socketContainer.Sockets;

            // We need to dynamically fetch the bounding box of main mesh as some chests have the correct one inside BIP01 "CHEST_*_0" and others inside "ZM_CHEST*"
            var meshFilters = _rootGo
                .GetComponentsInChildren<MeshFilter>() // Find all meshes
                .Where(i => !i.TryGetComponent(typeof(HVRGrabbable), out _)) // Filter out meshe, which is the lid
                .Where(i => i.gameObject.name.StartsWithIgnoreCase("ZM_") || i.gameObject.name.StartsWithIgnoreCase("BIP01")) // Use Gothic elements only. Ignore elements like HVR sockets.
                .Where(i => !i.gameObject.name.ContainsIgnoreCase("LOCK")) // Ignore lock element
                .ToArray();

            MeshFilter usedMesh;
            if (meshFilters.IsEmpty())
            {
                Logger.LogError($"No suitable mesh found for HVR Socket size calculation in {_rootGo.name}. Skipping...", LogCat.VR);
                return;
            }
            else if (meshFilters.Length >= 2)
            {
                Logger.LogWarning($"More than one feasible MeshFilter for HVR Socket size calculation found in {_rootGo.name}. Leveraging first (1)ZM_* or (2)BIP01 one.", LogCat.VR);

                // Based on mesh check of all containers in G1, the highest priority for the right mesh is a ZM_* GO if existing.
                var zmMesh = meshFilters.FirstOrDefault(i => i.gameObject.name.StartsWithIgnoreCase("ZM_"));
                usedMesh = zmMesh ?? meshFilters.First();
            }
            else
            {
                usedMesh = meshFilters.First();
            }
            
            var mainMeshBounds = usedMesh!.sharedMesh.bounds;
            
            // Set collectorCollider's size to perfectly align with container.
            _collectorCollider.size = new Vector3(mainMeshBounds.size.x, _collectorCollider.size.y, mainMeshBounds.size.z);
            
            // Move all Slots nearly towards height of container (*1,5 == 75% close to top).
            _socketContainer.transform.localPosition = new (0, mainMeshBounds.center.y * 1.5f, 0);

            var centeredMainBound = new Bounds(Vector3.zero, mainMeshBounds.size);
            
            // Align all sockets in row+column based on actual sizes.
            var rowWidth = (centeredMainBound.size.x - 2*_socketCollectionMargin) / _socketsPerRow;
            var rowDepth = (centeredMainBound.size.z - 2*_socketCollectionMargin) / _socketRows;
            
            // Calculate center of a grid-element for a socket ring entry.
            var middleAlignmentX = (rowWidth - _socketRadius * 2) / 2;
            var middleAlignmentZ = (rowDepth - _socketRadius * 2) / 2;
            
            // Loop variables
            var currentRow = 0;
            var currentColumn = 0;
            
            foreach (var socket in sockets)
            {
                // Calculation
                // _socketCollectionMargin - Left aligned margin for the whole collection
                // _socketRadius - Location is based on a 50% shift into the desired area as the drawing of a circle starts centered
                // middleAlignment* - Centered location within a grid for an element
                // min.x - Start point to calculate from left top
                socket.transform.localPosition = new (
                    _socketCollectionMargin + _socketRadius + middleAlignmentX + centeredMainBound.min.x + rowWidth * currentColumn,
                    0,
                    _socketCollectionMargin + _socketRadius + middleAlignmentZ + centeredMainBound.min.z + rowDepth * currentRow
                );

                currentColumn++;
                if (currentColumn >= _socketsPerRow)
                {
                    currentColumn = 0;
                    currentRow++;
                }
            }
        }
        
        private IEnumerator InitializeContent()
        {
            var contents = _vobContainer.Contents;

            if (string.IsNullOrEmpty(contents))
            {
                yield break;
            }

            var contentItems = ParseContent(contents);

            PreSpawnContent(out AudioClip grabAudio, out AudioClip releaseAudio);
            yield return SpawnContent(contentItems);
            yield return PostSpawnContent(grabAudio, releaseAudio);
        }
        
        private List<ContentItem> ParseContent(string contents)
        {
            List<ContentItem> result = new();
            
            var items = contents.Split(_itemNameSeparators);
        
            foreach (var item in items)
            {
                var count = 1;
                var nameCountSplit = item.Split(_itemCountSeparators);
        
                if (nameCountSplit.Length != 1)
                {
                    count = int.Parse(nameCountSplit[1]);
                }
        
                result.Add(new ContentItem
                {
                    Name = nameCountSplit[0],
                    Amount = count
                });
            }

            return result;
        }


        /// <summary>
        /// When we spawn items later, they will be automatically collected by the HVR Collector. It will put their meshes
        /// into place. But it will also play an audio file. We will temporarily remove this file until we're done adding the items.
        /// </summary>
        private void PreSpawnContent(out AudioClip grabAudio, out AudioClip releaseAudio)
        {
            // We assume that all Slots have the same sound. Therefore fetching first only.
            var firstSocket = _socketContainer.Sockets.First();
            grabAudio = firstSocket.AudioGrabbedFallback;
            releaseAudio = firstSocket.AudioReleasedFallback;
            
            foreach (var socket in _socketContainer.Sockets)
            {
                socket.AudioGrabbedFallback = null;
                socket.AudioReleasedFallback = null;
            }
        }

        private IEnumerator SpawnContent(List<ContentItem> contentItems)
        {
            var sockets = _socketContainer.Sockets;

            if (contentItems.Count > sockets.Count)
            {
                Logger.LogError("There are more items in a container than HVRSockets available. Please implement pagination to ensure everything is visible and can be grabbed!", LogCat.VR);
            }

            for (var i = 0; i < Math.Min(contentItems.Count, sockets.Count); i++)
            {
                var currentItem = contentItems[i];

                var zkVob = new Item
                {
                    Name = currentItem.Name,
                    Visual = new VisualMesh
                    {
                        Name = currentItem.Name
                    }
                };

                var vobContainer = _vobService.CreateItem(zkVob);

                // Wait 1 frame to ensure our mesh bounds can be calculated by HVR Socket.
                yield return null;
                
                PlaceObjectIntoContainer(vobContainer);
            }
        }

        private void PlaceObjectIntoContainer(VobContainer vobContainer)
        {
            var grabbable = vobContainer.Go.GetComponentInChildren<HVRGrabbable>(true);
            if (_socketContainer.TryFindAvailableSocket(grabbable, out var socket))
            {
                socket.TryGrab(grabbable, true, true);
                // We need to reset position as the element won't be placed inside it's parent in the chest.
                vobContainer.Go.transform.localPosition = Vector3.zero;
            }
        }
        
        private IEnumerator PostSpawnContent(AudioClip grabAudio, AudioClip releaseAudio)
        {
            // We wait for some time to ensure the objects are automatically snapped into place.
            yield return new WaitForSeconds(1f);
            
            foreach (var socket in _socketContainer.Sockets)
            {
                socket.AudioGrabbedFallback = grabAudio;
                socket.AudioReleasedFallback = releaseAudio;
            }
        }
        
        /// <summary>
        /// FIXME - Only temporary function. In the future we need to create a new Item to use it (e.g.) for saving later.
        /// FIXME - For now we stick with the mesh without properties only.
        /// 1. Create a new ZenKit.Vob.Item object
        /// 2. Call VobCreator.CreateItem(vob, ...)
        /// </summary>
        private GameObject TempCreateItem(ItemInstance itemInstance)
        {
            var go = _resourceCacheService.TryGetPrefabObject(PrefabType.VobItem);
            
            var mrm = _resourceCacheService.TryGetMultiResolutionMesh(itemInstance.Visual);
            return _meshService.CreateVob(itemInstance.Name, mrm, default, default, true, rootGo: go, useTextureArray: false);
        }
    }
}
#endif
