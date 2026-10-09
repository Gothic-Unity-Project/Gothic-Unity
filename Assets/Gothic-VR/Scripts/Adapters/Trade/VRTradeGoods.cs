#if GOTHIC_HVR_INSTALLED
using System.Collections;
using Gothic.Core;
using Gothic.Core.Adapters.Vob;
using Gothic.Core.Domain.Inventory;
using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Trade;
using Gothic.Core.Services.Vobs;
using Gothic.Core.Services.World;
using Gothic.VR.Adapters.Player;
using HurricaneVR.Framework.Core;
using HurricaneVR.Framework.Core.Grabbers;
using UnityEngine;
using ZenKit.Vobs;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Trade
{
    /// <summary>
    /// A trader's item taken out of his goods during a trade (not paid yet). Let go anywhere but the trader's side of
    /// the counter or his goods - dropped, put into the hero's backpack or holster - it goes back to the trader. Also
    /// when the trade ends.
    /// </summary>
    public class VRTradeGoods : MonoBehaviour
    {
        private const float _placeCheckDelay = 0.3f;

        public NpcContainer Trader { get; private set; }

        /// <summary>
        /// Paid, laid back into the goods, or already returned - nothing to give back any more.
        /// </summary>
        public bool IsSettled { get; set; }

        private HVRGrabbable _grabbable;

        public void Init(NpcContainer trader)
        {
            Trader = trader;
        }

        private void Start()
        {
            _grabbable = GetComponentInChildren<HVRGrabbable>(true);
            if (_grabbable != null)
                _grabbable.Released.AddListener(OnReleased);
            GlobalEventDispatcher.TradeClosed.AddListener(OnTradeClosed);
        }

        private void OnDestroy()
        {
            if (_grabbable != null)
                _grabbable.Released.RemoveListener(OnReleased);
            GlobalEventDispatcher.TradeClosed.RemoveListener(OnTradeClosed);
        }

        private void OnReleased(HVRGrabberBase grabber, HVRGrabbable grabbable)
        {
            if (IsSettled || grabber is not HVRHandGrabber)
                return;
            StartCoroutine(CheckWhereItWent());
        }

        private IEnumerator CheckWhereItWent()
        {
            yield return new WaitForSeconds(_placeCheckDelay);
            if (IsSettled || _grabbable == null || _grabbable.IsBeingHeld)
                yield break;

            if (IsOnCounterOrInGoods())
                yield break;

            Logger.Log($"[Trade] {name} left outside the counter - back to the trader", LogCat.VR);
            ReturnToTrader();
        }

        private void OnTradeClosed(TradeSession session)
        {
            if (session.Trader != Trader || IsSettled)
                return;

            // Lying in the goods (a view of the trader's inventory) or on the counter (TradeService gives the offers
            // back) - nothing to return here.
            if (IsOnCounterOrInGoods())
            {
                IsSettled = true;
                return;
            }
            ReturnToTrader();
        }

        private bool IsOnCounterOrInGoods()
        {
            var socket = _grabbable != null ? _grabbable.Socket : null;
            return socket != null && (socket.GetComponentInParent<VRTradeCounter>() != null ||
                                      socket.GetComponentInParent<VRBackpack>() is { IsTradeBackpack: true });
        }

        /// <summary>
        /// Back into the trader's inventory, the world item is gone.
        /// </summary>
        public void ReturnToTrader()
        {
            if (IsSettled)
                return;
            IsSettled = true;

            var container = GetComponentInParent<VobLoader>(true)?.Container;
            var item = container?.VobAs<IItem>();
            if (item == null)
                return;

            // Held items count as the hero's - releasing takes it out of his inventory again.
            if (_grabbable != null && (_grabbable.IsBeingHeld || _grabbable.IsSocketed))
                _grabbable.ForceRelease();

            var instanceName = !string.IsNullOrEmpty(item.Instance) ? item.Instance : item.Name;
            new NpcInventoryOwner(Trader).Add(instanceName, Mathf.Max(1, item.Amount));
            RemoveFromWorld(container);
        }

        public static void RemoveFromWorld(VobContainer container)
        {
            ReflexProjectInstaller.DIContainer.Resolve<SaveGameService>().UntrackLooseItem(container);
            ReflexProjectInstaller.DIContainer.Resolve<VobService>().RemoveWorldItem(container);
        }
    }
}
#endif
