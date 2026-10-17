#if GOTHIC_HVR_INSTALLED
using System.Collections;
using System.Collections.Generic;
using Gothic.Core;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Trade;
using Gothic.Core.Models.Vm;
using Gothic.Core.Const;
using Gothic.Core.Services.Config;
using Gothic.Core.Services.Player;
using Gothic.Core.Services.Vobs;
using Gothic.Core.Services.Trade;
using Gothic.VR.Adapters.Player;
using HurricaneVR.Framework.Core;
using HurricaneVR.Framework.Core.Grabbers;
using HurricaneVR.Framework.Core.Sockets;
using HurricaneVR.Framework.Core.UI;
using Reflex.Attributes;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using ZenKit.Daedalus;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Trade
{
    /// <summary>
    /// V1 (DeveloperConfig.EnableVrTrade): the trade counter between the hero and the trader. Left: what the hero gives
    /// (sells), right: what he takes (buys), behind: the trader's goods (his backpack, without his currency and
    /// equipped items). The middle shows the difference in currency, OK settles it (TradeService), X closes.
    /// Offered items are reserved - TradeService gives them back on close.
    /// Each side is a view of its TradeOffer, like the backpack is a view of the inventory: an item laid into any slot
    /// joins the offer (the same items stack into one), the slots are rebuilt from the offer. Three slots show the
    /// offer, the arrows move it by one; the slot next to the middle is always empty, to lay items into.
    /// </summary>
    public class VRTradeCounter : MonoBehaviour
    {
        [Inject] private readonly TradeService _tradeService;
        [Inject] private readonly PlayerService _playerService;
        [Inject] private readonly AudioService _audioService;
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly VobService _vobService;

        // Big - a backpack thrown a bit off still lands on it.
        private const float _goodsTableWidth = 10f;
        private const float _goodsTableDepth = 8f;
        private const float _goodsTableThickness = 0.05f;
        private const int _visibleSlotsPerSide = 3;
        private const float _socketSpacing = 0.2f;
        private const float _socketScale = 1.6f;
        private const float _textFontSize = 17.4f;
        // TMP's default font shows nothing at runtime - the backpack's arrows use this one too.
        private const string _arrowFontPath = "Fonts & Materials/LiberationSans SDF";
        private static readonly Color _canPayColor = new(0.45f, 1f, 0.45f);
        private static readonly Color _cannotPayColor = new(1f, 0.4f, 0.35f);

        private enum Side
        {
            Give,
            Take
        }

        private readonly struct Placed
        {
            public readonly Side Side;
            public readonly string ItemName;
            public readonly int Amount;

            public Placed(Side side, string itemName, int amount)
            {
                Side = side;
                ItemName = itemName;
                Amount = amount;
            }
        }

        private TradeSession _session;
        private readonly List<HVRSocket> _sockets = new();
        private readonly Dictionary<HVRSocket, Side> _socketSides = new();
        // Per side: the slots showing the offer (left to right) and the first offer item they show.
        private readonly Dictionary<Side, List<HVRSocket>> _slots = new() { [Side.Give] = new(), [Side.Take] = new() };
        private readonly Dictionary<Side, int> _offsets = new() { [Side.Give] = 0, [Side.Take] = 0 };
        private readonly Dictionary<Side, TMP_Text> _scrollTexts = new();
        private Coroutine _refresh;
        private bool _isRefreshDirty;
        private readonly Dictionary<HVRGrabbable, Placed> _placed = new();
        private bool _isIgnoringSockets;
        private VRBackpack _goods;
        private GameObject _goodsRoot;
        private Canvas _canvas;
        private TMP_Text _giveText;
        private TMP_Text _takeText;
        private TMP_Text _balanceText;

        public static VRTradeCounter Spawn(TradeSession session, Transform head)
        {
            var go = new GameObject($"TradeCounter_{session.Trader.Instance.GetName(NpcNameSlot.Slot0)}");
            var counter = go.AddComponent<VRTradeCounter>();
            counter.Init(session, head);
            return counter;
        }

        private void Init(TradeSession session, Transform head)
        {
            this.Inject();
            _session = session;

            // Between the hero and the trader, at hand height, facing the trader.
            var toTrader = session.Trader.Go.transform.position - head.position;
            toTrader.y = 0f;
            var forward = toTrader.sqrMagnitude > 0.01f ? toTrader.normalized : head.forward;
            transform.SetPositionAndRotation(head.position + forward * 0.55f + Vector3.down * 0.45f,
                Quaternion.LookRotation(forward));

            CreateOfferZones();
            CreateGoods();
            CreateTexts();
            CreateButtons();
            // Like an open backpack: hovering the slots (and the goods lying here) shows the item's details.
            if (_configService.Dev.EnableItemDetailsPopup)
                gameObject.AddComponent<VRItemDetailsPopup>();

            GlobalEventDispatcher.TradeOfferChanged.AddListener(OnOfferChanged);
            GlobalEventDispatcher.TradeCommitted.AddListener(OnCommitted);
            GlobalEventDispatcher.TradeClosed.AddListener(OnClosed);
            PlaySound(_audioService.InvOpen);
            UpdateTexts();
        }

        private void OnDestroy()
        {
            GlobalEventDispatcher.TradeOfferChanged.RemoveListener(OnOfferChanged);
            GlobalEventDispatcher.TradeCommitted.RemoveListener(OnCommitted);
            GlobalEventDispatcher.TradeClosed.RemoveListener(OnClosed);
            if (_canvas != null && HVRInputModule.Instance != null)
                HVRInputModule.Instance.RemoveCanvas(_canvas);
        }

        /// <summary>
        /// The trader can't trade any more - everything goes back.
        /// </summary>
        private void Update()
        {
            if (_session == null || _tradeService.Current != _session)
                return;

            var trader = _session.Trader;
            var isOut = trader.Go == null || trader.Props.BodyState is VmGothicEnums.BodyState.BsDead
                            or VmGothicEnums.BodyState.BsUnconscious ||
                        (VmGothicEnums.WeaponState)trader.Vob.FightMode != VmGothicEnums.WeaponState.NoWeapon;
            if (isOut)
            {
                Logger.Log("[Trade] The trader can't trade any more - closed", LogCat.VR);
                _tradeService.Cancel();
            }
        }

        private void CreateOfferZones()
        {
            var socketPrefab = _session.Trader.Go.GetComponentInChildren<VRNpcLoot>(true)?.SocketPrefab;
            if (socketPrefab == null)
            {
                Logger.LogWarning("[Trade] No socket prefab (VRNpcLoot on the trader) - no offer zones", LogCat.VR);
                return;
            }

            // Middle out: the always empty slot, then the offer's slots - left to right on both sides.
            CreateSocket(socketPrefab, Side.Give, -_socketSpacing);
            CreateSocket(socketPrefab, Side.Take, _socketSpacing);
            for (var i = 0; i < _visibleSlotsPerSide; i++)
            {
                _slots[Side.Give].Add(CreateSocket(socketPrefab, Side.Give,
                    -_socketSpacing * (_visibleSlotsPerSide + 1 - i)));
                _slots[Side.Take].Add(CreateSocket(socketPrefab, Side.Take, _socketSpacing * (i + 2)));
            }
        }

        /// <summary>
        /// The x of the middle of a side's offer slots.
        /// </summary>
        private static float GetListCenterX(Side side)
        {
            var x = _socketSpacing * (_visibleSlotsPerSide + 3) / 2f;
            return side == Side.Give ? -x : x;
        }

        private HVRSocket CreateSocket(GameObject socketPrefab, Side side, float x)
        {
            var socketGo = Instantiate(socketPrefab, transform);
            socketGo.transform.localPosition = new Vector3(x, 0f, 0f);
            socketGo.transform.localRotation = Quaternion.identity;
            socketGo.transform.localScale = Vector3.one * _socketScale;

            var socket = socketGo.GetComponentInChildren<HVRSocket>();
            if (socket == null)
                return null;
            socket.Grabbed.AddListener(OnItemPlaced);
            socket.Released.AddListener(OnItemTaken);
            _sockets.Add(socket);
            _socketSides[socket] = side;
            return socket;
        }

        private void CreateGoods()
        {
            var position = transform.TransformPoint(new Vector3(0f, 0.1f, 0.35f));
            _goods = VRBackpack.SpawnCopy(position, Quaternion.LookRotation(-transform.forward),
                $"TradeGoods_{_session.Trader.Instance.GetName(NpcNameSlot.Slot0)}", out _goodsRoot);
            if (_goods == null)
                return;

            // It stays on the counter.
            _goodsRoot.transform.SetParent(transform, true);
            foreach (var body in _goodsRoot.GetComponentsInChildren<Rigidbody>())
                body.isKinematic = true;
            _goods.SetTradeOwner(_session.Trader, _session.TraderGoods);

            if (_configService.Dev.EnableTradeGoodsTable)
                CreateGoodsTable();
        }

        /// <summary>
        /// An invisible table right under the goods: put down, the backpack lands on it at hand height instead of on
        /// the floor. Collider layer overrides: the table excludes every layer, the backpack's own colliders include
        /// the table's layer with a higher priority - so only they touch it.
        /// </summary>
        private void CreateGoodsTable()
        {
            var backpackBody = _goodsRoot.GetComponentInChildren<Rigidbody>();
            var colliders = new List<Collider>();
            foreach (var collider in _goodsRoot.GetComponentsInChildren<Collider>())
            {
                if (!collider.isTrigger && collider.attachedRigidbody == backpackBody)
                    colliders.Add(collider);
            }
            if (backpackBody == null || colliders.Count == 0)
            {
                Logger.LogWarning("[Trade] Goods backpack has no colliders - no goods table", LogCat.VR);
                return;
            }

            var bounds = colliders[0].bounds;
            foreach (var collider in colliders)
                bounds.Encapsulate(collider.bounds);

            var tableGo = new GameObject("_TradeGoodsTable");
            tableGo.layer = Constants.IgnoreRaycastLayer;
            tableGo.transform.SetParent(transform, false);
            // The counter only turns around the up axis - its local y is the world height above it.
            var top = bounds.min.y - transform.position.y;
            var depth = transform.InverseTransformPoint(bounds.center).z;
            tableGo.transform.localPosition = new Vector3(0f, top - _goodsTableThickness / 2f, depth);

            var table = tableGo.AddComponent<BoxCollider>();
            table.size = new Vector3(_goodsTableWidth, _goodsTableThickness, _goodsTableDepth);
            table.excludeLayers = ~0;
            table.layerOverridePriority = 0;

            foreach (var collider in colliders)
            {
                collider.includeLayers = collider.includeLayers.value | (1 << Constants.IgnoreRaycastLayer.value);
                collider.layerOverridePriority = Mathf.Max(collider.layerOverridePriority, 1);
            }
            Logger.Log($"[Trade] Goods table under the backpack ({colliders.Count} colliders)", LogCat.VR);
        }

        private void CreateTexts()
        {
            _giveText = CreateText("GiveValue", new Vector3(GetListCenterX(Side.Give), 0.14f, 0f));
            _takeText = CreateText("TakeValue", new Vector3(GetListCenterX(Side.Take), 0.14f, 0f));
            _scrollTexts[Side.Give] = CreateText("GiveScroll", new Vector3(GetListCenterX(Side.Give), -0.13f, 0f));
            _scrollTexts[Side.Take] = CreateText("TakeScroll", new Vector3(GetListCenterX(Side.Take), -0.13f, 0f));
            CreateText("GivePool", new Vector3(-_socketSpacing, 0.14f, 0f)).text = "+";
            CreateText("TakePool", new Vector3(_socketSpacing, 0.14f, 0f)).text = "+";
            _balanceText = CreateText("Balance", new Vector3(0f, 0.08f, 0f));

            // Whose side is whose: the hero's name over his offer, the trader's over the goods he gives.
            var heroName = _playerService.HeroContainer?.Instance.GetName(NpcNameSlot.Slot0) ?? "";
            CreateText("GiveName", new Vector3(GetListCenterX(Side.Give), 0.2f, 0f)).text = heroName;
            CreateText("TakeName", new Vector3(GetListCenterX(Side.Take), 0.2f, 0f)).text =
                _session.Trader.Instance.GetName(NpcNameSlot.Slot0);
        }

        private TMP_Text CreateText(string goName, Vector3 localPosition)
        {
            var textGo = new GameObject(goName);
            textGo.transform.SetParent(transform, false);
            textGo.transform.localPosition = localPosition;
            // Readable from the hero's side.
            textGo.transform.localRotation = Quaternion.identity;
            textGo.transform.localScale = Vector3.one * 0.02f;

            var text = textGo.AddComponent<TextMeshPro>();
            Gothic.VR.Adapters.UI.VRGothicText.Apply(text);
            text.fontSize = _textFontSize;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.fontStyle = FontStyles.Bold;
            text.color = Color.white;
            return text;
        }

        private void CreateButtons()
        {
            var canvasGo = new GameObject("TradeButtons", typeof(RectTransform));
            canvasGo.transform.SetParent(transform, false);
            canvasGo.transform.localPosition = new Vector3(0f, -0.06f, 0f);
            canvasGo.transform.localScale = Vector3.one * 0.001f;

            _canvas = canvasGo.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            canvasGo.AddComponent<GraphicRaycaster>();
            ((RectTransform)canvasGo.transform).sizeDelta =
                new Vector2(_socketSpacing * 1000f * (_visibleSlotsPerSide + 3) * 2f, 80f);

            CreateButton(canvasGo.transform, "OK", new Vector2(-60f, 0f), new Color(0.2f, 0.5f, 0.2f, 0.9f), OnAccept);
            CreateButton(canvasGo.transform, "X", new Vector2(60f, 0f), new Color(0.5f, 0.2f, 0.2f, 0.9f), OnClose);

            // Under each side's slots: move its offer by one (the canvas has 1000 px per meter).
            var arrowColor = new Color(0.25f, 0.25f, 0.25f, 0.9f);
            foreach (var side in new[] { Side.Give, Side.Take })
            {
                var centerPx = GetListCenterX(side) * 1000f;
                var halfPx = _socketSpacing * 1000f * _visibleSlotsPerSide / 2f;
                CreateButton(canvasGo.transform, "<", new Vector2(centerPx - halfPx, 0f), arrowColor,
                    () => Scroll(side, -1), false);
                CreateButton(canvasGo.transform, ">", new Vector2(centerPx + halfPx, 0f), arrowColor,
                    () => Scroll(side, 1), false);
            }

            if (HVRInputModule.Instance != null)
                HVRInputModule.Instance.AddCanvas(_canvas);
        }

        private static void CreateButton(Transform parent, string label, Vector2 position, Color color,
            UnityEngine.Events.UnityAction onClick, bool isGothicFont = true)
        {
            var buttonGo = new GameObject($"Button_{label}", typeof(RectTransform));
            buttonGo.transform.SetParent(parent, false);
            var rect = (RectTransform)buttonGo.transform;
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(100f, 70f);

            var image = buttonGo.AddComponent<Image>();
            image.color = color;
            var button = buttonGo.AddComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(buttonGo.transform, false);
            var textRect = (RectTransform)textGo.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
            var text = textGo.AddComponent<TextMeshProUGUI>();
            // Gothic's font has no arrows.
            if (isGothicFont)
                Gothic.VR.Adapters.UI.VRGothicText.Apply(text);
            else
                text.font = Resources.Load<TMP_FontAsset>(_arrowFontPath);
            text.text = label;
            text.fontSize = 40;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
        }

        private void OnItemPlaced(HVRGrabberBase grabber, HVRGrabbable grabbable)
        {
            if (_isIgnoringSockets || grabber is not HVRSocket socket || !_socketSides.TryGetValue(socket, out var side) ||
                _placed.ContainsKey(grabbable))
                return;

            var container = grabbable.GetComponentInParent<VobLoader>(true)?.Container;
            var item = container?.VobAs<IItem>();
            if (item == null)
                return;

            var itemName = !string.IsNullOrEmpty(item.Instance) ? item.Instance : item.Name;
            var amount = Mathf.Max(1, item.Amount);
            var tradeGoods = grabbable.GetComponentInParent<VRTradeGoods>();

            if (side == Side.Give)
            {
                // The trader's goods on the hero's side - back to him. Currency is paid automatically.
                if (tradeGoods != null)
                {
                    ReleaseIgnored(grabbable);
                    tradeGoods.ReturnToTrader();
                    return;
                }
                if (itemName.EqualsIgnoreCase(_session.CurrencyInstance))
                {
                    ReturnToHero(grabbable, container, itemName, amount);
                    return;
                }
                JoinOffer(grabbable, container, null);
                _tradeService.OfferFromPlayer(itemName, amount);
            }
            else
            {
                // Only the trader's goods can be taken.
                if (tradeGoods == null)
                {
                    ReturnToHero(grabbable, container, itemName, amount);
                    return;
                }
                JoinOffer(grabbable, container, tradeGoods);
                _tradeService.OfferFromTrader(itemName, amount);
            }
            ShowOfferItem(side, itemName);
            RequestRefresh();
        }

        /// <summary>
        /// The laid item becomes part of its side's offer - the slots show the offer, rebuilt in RefreshSlots.
        /// </summary>
        private void JoinOffer(HVRGrabbable grabbable, VobContainer container, VRTradeGoods tradeGoods)
        {
            if (tradeGoods != null)
                tradeGoods.IsSettled = true;
            ReleaseIgnored(grabbable);
            VRTradeGoods.RemoveFromWorld(container);
        }

        /// <summary>
        /// Moves a side's slots so they show this offer item (a new one is added at the end).
        /// </summary>
        private void ShowOfferItem(Side side, string itemName)
        {
            var items = GetOffer(side).Items;
            for (var i = 0; i < items.Count; i++)
            {
                if (!items[i].Name.EqualsIgnoreCase(itemName))
                    continue;
                if (i < _offsets[side])
                    _offsets[side] = i;
                else if (i >= _offsets[side] + _visibleSlotsPerSide)
                    _offsets[side] = i - _visibleSlotsPerSide + 1;
                return;
            }
        }

        private void Scroll(Side side, int step)
        {
            if (_session == null)
                return;
            var maxOffset = Mathf.Max(0, GetOffer(side).Items.Count - _visibleSlotsPerSide);
            var offset = Mathf.Clamp(_offsets[side] + step, 0, maxOffset);
            if (offset == _offsets[side])
                return;
            _offsets[side] = offset;
            RequestRefresh();
        }

        private TradeOffer GetOffer(Side side)
        {
            return side == Side.Give ? _session.PlayerOffer : _session.TraderOffer;
        }

        private void OnItemTaken(HVRGrabberBase grabber, HVRGrabbable grabbable)
        {
            if (_isIgnoringSockets || !_placed.Remove(grabbable, out var placed))
                return;

            if (placed.Side == Side.Give)
                _tradeService.WithdrawFromPlayerOffer(placed.ItemName, placed.Amount);
            else
                _tradeService.WithdrawFromTraderOffer(placed.ItemName, placed.Amount);
            RequestRefresh();
        }

        private void RequestRefresh()
        {
            if (_refresh != null)
            {
                _isRefreshDirty = true;
                return;
            }
            _refresh = StartCoroutine(RefreshSlots());
        }

        /// <summary>
        /// Like VRBackpack.UpdateSockets: empty the slots, then lay the shown part of each offer into them.
        /// </summary>
        private IEnumerator RefreshSlots()
        {
            RemovePlacedItems();
            // Released and removed items are gone only in the next frame.
            yield return null;

            if (_session != null && _tradeService.Current == _session)
            {
                _isIgnoringSockets = true;
                try
                {
                    FillSlots(Side.Give);
                    FillSlots(Side.Take);
                }
                catch (System.Exception e)
                {
                    Logger.LogError($"[Trade] Refilling the counter failed: {e}", LogCat.VR);
                }
                _isIgnoringSockets = false;
            }

            _refresh = null;
            if (_isRefreshDirty)
            {
                _isRefreshDirty = false;
                RequestRefresh();
            }
        }

        private void FillSlots(Side side)
        {
            var items = GetOffer(side).Items;
            var maxOffset = Mathf.Max(0, items.Count - _visibleSlotsPerSide);
            _offsets[side] = Mathf.Clamp(_offsets[side], 0, maxOffset);

            var slots = _slots[side];
            for (var i = 0; i < slots.Count; i++)
            {
                var index = _offsets[side] + i;
                if (slots[i] == null || index >= items.Count)
                    continue;

                var item = items[index];
                var container = _vobService.CreateItem(new Item
                {
                    Name = item.Name,
                    Visual = new VisualMesh(),
                    Instance = item.Name,
                    Amount = item.Amount
                });
                // The trader's unpaid goods stay his when taken off the counter (VRTradeGoods).
                if (side == Side.Take)
                    container.Go.AddComponent<VRTradeGoods>().Init(_session.Trader);
                container.Go.GetComponentInChildren<Rigidbody>().isKinematic = false;
                var grabbable = container.Go.GetComponentInChildren<HVRGrabbable>();
                _placed[grabbable] = new Placed(side, item.Name, item.Amount);
                if (!slots[i].TryGrab(grabbable, true))
                {
                    _placed.Remove(grabbable);
                    Logger.LogWarning($"[Trade] {item.Name} couldn't go into a counter slot", LogCat.VR);
                    VRTradeGoods.RemoveFromWorld(container);
                }
            }

            var scrollText = _scrollTexts.GetValueOrDefault(side);
            if (scrollText != null)
                scrollText.text = items.Count > _visibleSlotsPerSide
                    ? $"{_offsets[side] + 1}-{_offsets[side] + _visibleSlotsPerSide}/{items.Count}"
                    : "";
        }

        private void ReleaseIgnored(HVRGrabbable grabbable)
        {
            _isIgnoringSockets = true;
            grabbable.ForceRelease();
            _isIgnoringSockets = false;
        }

        /// <summary>
        /// Not tradeable here: back into the hero's inventory (the hand let go of it, so it isn't counted any more).
        /// </summary>
        private void ReturnToHero(HVRGrabbable grabbable, VobContainer container, string itemName, int amount)
        {
            ReleaseIgnored(grabbable);
            _playerService.AddItem(itemName, amount);
            VRTradeGoods.RemoveFromWorld(container);
        }

        private void OnOfferChanged(TradeSession session)
        {
            if (session == _session)
                UpdateTexts();
        }

        private void UpdateTexts()
        {
            if (_balanceText == null || _tradeService.Current != _session)
                return;

            var currencyName = _tradeService.GetCurrencyDisplayName();
            _giveText.text = $"+{_tradeService.GetPlayerOfferValue()}";
            _takeText.text = $"-{_tradeService.GetTraderOfferValue()}";

            var balance = _tradeService.GetBalance();
            var sign = balance > 0 ? "-" : balance < 0 ? "+" : "";
            _balanceText.text = $"{sign}{Mathf.Abs(balance)} {currencyName}  ({_tradeService.GetPlayerCurrency()})";
            _balanceText.color = balance <= _tradeService.GetPlayerCurrency() ? _canPayColor : _cannotPayColor;
        }

        private void OnAccept()
        {
            if (_tradeService.Current != _session)
                return;

            if (_tradeService.TryCommit())
            {
                PlaySound(_audioService.InvClose);
                _tradeService.Cancel();
            }
        }

        private void OnClose()
        {
            if (_tradeService.Current == _session)
                _tradeService.Cancel();
        }

        /// <summary>
        /// The offers changed hands in TradeService - the items lying on the counter are just gone.
        /// </summary>
        private void OnCommitted(TradeSession session)
        {
            if (session == _session)
                RemovePlacedItems();
        }

        private void OnClosed(TradeSession session)
        {
            if (session != _session)
                return;

            // TradeService gave the offers back already - the items on the counter only need to disappear.
            RemovePlacedItems();
            if (_goods != null)
                _goods.Despawn(_goodsRoot);
            _session = null;
            Destroy(gameObject);
        }

        private void RemovePlacedItems()
        {
            _isIgnoringSockets = true;
            foreach (var socket in _sockets)
            {
                if (socket == null || !socket.IsGrabbing)
                    continue;

                var grabbable = socket.GrabbedTarget;
                var container = grabbable.GetComponentInParent<VobLoader>(true)?.Container;
                var tradeGoods = grabbable.GetComponentInParent<VRTradeGoods>();
                if (tradeGoods != null)
                    tradeGoods.IsSettled = true;
                socket.ForceRelease();
                if (container != null)
                    VRTradeGoods.RemoveFromWorld(container);
            }
            _placed.Clear();
            _isIgnoringSockets = false;
        }

        private void PlaySound(SoundEffectInstance sound)
        {
            var clip = sound != null ? _audioService.CreateAudioClip(sound) : null;
            if (clip != null)
                AudioSource.PlayClipAtPoint(clip, transform.position);
        }
    }
}
#endif
