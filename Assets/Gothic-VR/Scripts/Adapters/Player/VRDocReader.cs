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
using ZenKit;
using ZenKit.Daedalus;

namespace Gothic.VR.Adapters.Player
{
    /// <summary>
    /// Reading in VR: the trigger on a held book, letter or map uses it like Gothic's inventory does (its use function
    /// builds and shows the document, VRDocViewer). The trigger again or letting go of it closes it. A document its use
    /// animation destroys (G1's sealed letter to the fire mages) is used up when it's closed.
    /// A page a mob shows (G2's bookstands) closes on the next touch of the mob or 3 m away (found on BoroBongo's
    /// branch).
    /// </summary>
    public class VRDocReader : MonoBehaviour
    {
        private const float _mobDocCloseDistance = 3f;

        [Inject] private readonly IVrInput _vrInput;
        [Inject] private readonly VRPlayerService _vrPlayerService;
        [Inject] private readonly NpcService _npcService;
        [Inject] private readonly NpcInventoryService _npcInventoryService;
        [Inject] private readonly DocService _docService;
        [Inject] private readonly TextureCacheService _textureCacheService;
        [Inject] private readonly FontService _fontService;

        // The held document that is shown, to close the viewer once it's let go.
        private GameObject _shownDocItem;
        private bool _isShownDocUsedUp;
        private bool _isReadingHeldDoc;
        // Where the player stood when a mob's page opened, null while none is shown.
        private Vector3? _mobDocPlayerPosition;

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
            if (_mobDocPlayerPosition != null && (!VRDocViewer.IsShown || IsAwayFromMobDoc()))
                CloseMobDoc();

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

            if (_shownDocItem != null)
                Close();

            _shownDocItem = heldItem;
            _isShownDocUsedUp = _npcInventoryService.IsUsedUpOnUse(item);
            _isReadingHeldDoc = true;
            try
            {
                _npcInventoryService.UseItem(_npcService.GetHeroContainer(), item);
            }
            finally
            {
                _isReadingHeldDoc = false;
            }
        }

        private void OnDocShown(DocModel doc)
        {
            if (!_isReadingHeldDoc)
            {
                // A mob's page: touching the mob again runs its script again - that closes the page instead.
                if (_mobDocPlayerPosition != null)
                {
                    CloseMobDoc();
                    return;
                }

                if (_shownDocItem != null)
                    Close();
                _mobDocPlayerPosition = GetPlayerPosition();
            }
            else
            {
                CloseMobDoc();
            }

            VRDocViewer.Show(doc, _docService, _textureCacheService, _fontService,
                _npcService.GetHeroGameObject()?.transform);
        }

        private void CloseMobDoc()
        {
            if (_mobDocPlayerPosition == null)
                return;

            _mobDocPlayerPosition = null;
            VRDocViewer.Hide();
        }

        private bool IsAwayFromMobDoc()
        {
            var position = GetPlayerPosition();
            return position != null && Vector3.Distance(position.Value, _mobDocPlayerPosition.Value) >
                _mobDocCloseDistance;
        }

        private static Vector3? GetPlayerPosition()
        {
            return Camera.main != null ? Camera.main.transform.position : null;
        }

        private void Close()
        {
            // Used up once the reading ends, not while it's in front of the player (found on BoroBongo's branch).
            if (_isShownDocUsedUp && _shownDocItem != null)
                _vrPlayerService.UseUpItem(_shownDocItem);

            _shownDocItem = null;
            _isShownDocUsedUp = false;
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
