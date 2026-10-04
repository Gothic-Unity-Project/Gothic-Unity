#if GOTHIC_HVR_INSTALLED
using System.Collections.Generic;
using Gothic.Core;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Trade;
using Gothic.Core.Models.Vm;
using Gothic.Core.Services.Player;
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
    /// </summary>
    public class VRTradeCounter : MonoBehaviour
    {
        [Inject] private readonly TradeService _tradeService;
        [Inject] private readonly PlayerService _playerService;
        [Inject] private readonly AudioService _audioService;

        private const int _socketsPerSide = 3;
        private const float _socketSpacing = 0.2f;
        private const float _socketScale = 1.6f;
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

            for (var i = 0; i < _socketsPerSide; i++)
            {
                CreateSocket(socketPrefab, Side.Give, -_socketSpacing * (i + 1));
                CreateSocket(socketPrefab, Side.Take, _socketSpacing * (i + 1));
            }
        }

        private void CreateSocket(GameObject socketPrefab, Side side, float x)
        {
            var socketGo = Instantiate(socketPrefab, transform);
            socketGo.transform.localPosition = new Vector3(x, 0f, 0f);
            socketGo.transform.localRotation = Quaternion.identity;
            socketGo.transform.localScale = Vector3.one * _socketScale;

            var socket = socketGo.GetComponentInChildren<HVRSocket>();
            if (socket == null)
                return;
            socket.Grabbed.AddListener(OnItemPlaced);
            socket.Released.AddListener(OnItemTaken);
            _sockets.Add(socket);
            _socketSides[socket] = side;
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
        }

        private void CreateTexts()
        {
            _giveText = CreateText("GiveValue", new Vector3(-_socketSpacing * 2f, 0.14f, 0f));
            _takeText = CreateText("TakeValue", new Vector3(_socketSpacing * 2f, 0.14f, 0f));
            _balanceText = CreateText("Balance", new Vector3(0f, 0.08f, 0f));

            // Whose side is whose: the hero's name over his offer, the trader's over the goods he gives.
            var heroName = _playerService.HeroContainer?.Instance.GetName(NpcNameSlot.Slot0) ?? "";
            CreateText("GiveName", new Vector3(-_socketSpacing * 2f, 0.2f, 0f)).text = heroName;
            CreateText("TakeName", new Vector3(_socketSpacing * 2f, 0.2f, 0f)).text =
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
            text.fontSize = 12;
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
            ((RectTransform)canvasGo.transform).sizeDelta = new Vector2(240f, 80f);

            CreateButton(canvasGo.transform, "OK", new Vector2(-60f, 0f), new Color(0.2f, 0.5f, 0.2f, 0.9f), OnAccept);
            CreateButton(canvasGo.transform, "X", new Vector2(60f, 0f), new Color(0.5f, 0.2f, 0.2f, 0.9f), OnClose);

            if (HVRInputModule.Instance != null)
                HVRInputModule.Instance.AddCanvas(_canvas);
        }

        private static void CreateButton(Transform parent, string label, Vector2 position, Color color,
            UnityEngine.Events.UnityAction onClick)
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
            Gothic.VR.Adapters.UI.VRGothicText.Apply(text);
            text.text = label;
            text.fontSize = 40;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
        }

        private void OnItemPlaced(HVRGrabberBase grabber, HVRGrabbable grabbable)
        {
            if (_isIgnoringSockets || grabber is not HVRSocket socket || !_socketSides.TryGetValue(socket, out var side))
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
                _placed[grabbable] = new Placed(Side.Give, itemName, amount);
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
                _placed[grabbable] = new Placed(Side.Take, itemName, amount);
                _tradeService.OfferFromTrader(itemName, amount);
            }
        }

        private void OnItemTaken(HVRGrabberBase grabber, HVRGrabbable grabbable)
        {
            if (_isIgnoringSockets || !_placed.Remove(grabbable, out var placed))
                return;

            if (placed.Side == Side.Give)
                _tradeService.WithdrawFromPlayerOffer(placed.ItemName, placed.Amount);
            else
                _tradeService.WithdrawFromTraderOffer(placed.ItemName, placed.Amount);
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
