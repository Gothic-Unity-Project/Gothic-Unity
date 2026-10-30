using Gothic.Core;
using Gothic.Core.Adapters.Properties.Vobs;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Models.Doc;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Caches;
using Gothic.Core.Services.Npc;
using Gothic.Core.Services.Player;
using Gothic.Core.Services.UI;
using Gothic.VR.Adapters.UI;
using Gothic.VR.Interaction.Models;
using Gothic.VR.Interaction.Ports;
using Gothic.VR.Services;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// Reading in VR: the trigger on a held book, letter or map uses it like Gothic's inventory does (its use function
    /// builds and shows the document, VRDocViewer). The trigger again or letting go of it closes it.
    /// </summary>
    public class VRDocReader : MonoBehaviour
    {
        [Inject] private readonly IVrInput _vrInput;
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly NpcInventoryService _npcInventoryService;
        [Inject] private readonly DocService _docService;
        [Inject] private readonly TextureCacheService _textureCacheService;
        [Inject] private readonly FontService _fontService;

        // The held document that is shown, to close the viewer once it's let go.
        private GameObject _shownDocItem;

        private void Start()
        {
            GlobalEventDispatcher.DocShown.AddListener(OnDocShown);
        }

        private void OnDestroy()
        {
            GlobalEventDispatcher.DocShown.RemoveListener(OnDocShown);
        }

        private void Update()
        {
            if (_shownDocItem != null && !IsHeld(_shownDocItem))
                Close();

            TryRead(VrHandSide.Left, _vrPlayerService.GrabbedItemLeft);
            TryRead(VrHandSide.Right, _vrPlayerService.GrabbedItemRight);
        }

        private void TryRead(VrHandSide side, GameObject heldItem)
        {
            if (heldItem == null || !_vrInput.GetButton(side, VrButton.Trigger).IsJustActivated)
                return;

            var item = GetDocument(heldItem);
            if (item == null)
                return;

            if (_shownDocItem == heldItem && VRDocViewer.IsShown)
            {
                Close();
                return;
            }

            _shownDocItem = heldItem;
            _npcInventoryService.UseItem(_npcService.GetHeroContainer(), item);
        }

        private void OnDocShown(DocModel doc)
        {
            VRDocViewer.Show(doc, _docService, _textureCacheService, _fontService,
                _npcService.GetHeroGameObject()?.transform);
        }

        private void Close()
        {
            _shownDocItem = null;
            VRDocViewer.Hide();
        }

        private bool IsHeld(GameObject item)
        {
            return item == _vrPlayerService.GrabbedItemLeft || item == _vrPlayerService.GrabbedItemRight;
        }

        private static ItemInstance GetDocument(GameObject heldItem)
        {
            var item = heldItem.GetComponentInParent<VobLoader>()?.Container?.PropsAs<VobItemProperties2>()?.Instance;
            if (item == null || item.Type != DaedalusInstanceType.Item)
                return null;

            return (VmGothicEnums.ItemFlags)item.MainFlag == VmGothicEnums.ItemFlags.ItemKatDocs ? item : null;
        }
    }
}
