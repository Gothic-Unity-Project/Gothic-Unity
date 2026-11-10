using Gothic.Core.Logging;
using Gothic.VR.Services;
using Reflex.Attributes;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.VR.Adapters.Vob.VobItem
{
    /// <summary>
    /// The Telekinesis spell in VR - a 2-trigger system that skips Daedalus (no mana, no scroll use): trigger 1 extends
    /// the hand's distance grab (VRPlayerService.ActivateTelekinesis), trigger 2 pulls the hovered item.
    /// Worker of VRRuneCaster (new VRTelekinesisSpell(...).Inject(), Dispose on destroy).
    /// </summary>
    public class VRTelekinesisSpell
    {
        // spellFXInstanceNames entry of the spell.
        public const string SpellName = "Telekinesis";

        [Inject] private readonly VRPlayerService _vrPlayerService;

        private const float _range = 5000f;

        private readonly VRSpellFx _fx;
        private bool _isPrepped;

        public VRTelekinesisSpell(VRSpellFx fx)
        {
            _fx = fx;
        }

        /// <summary>
        /// The cast trigger was pressed. True once the item is pulled - the grab's cast is used up.
        /// </summary>
        public bool OnTrigger()
        {
            if (!_isPrepped)
            {
                // Trigger 1: extend ForceGrab range so VRFocus highlights far items, loop SFX
                _isPrepped = true;
                Logger.Log("[VRTelekinesisSpell] PREP - ForceGrab range extended, aim and trigger to pull",
                    LogCat.VR);
                _fx.StartInvestSound();
                _vrPlayerService.TelekinesisDeactivated += OnTelekinesisEnded;
                _vrPlayerService.ActivateTelekinesis(_range);
                return false;
            }

            // Trigger 2: pull whatever the ForceGrabber is hovering (SFX stops via Grabbed event)
            Logger.Log("[VRTelekinesisSpell] PULL", LogCat.VR);
            _fx.PlayCastSound();
            _vrPlayerService.TryTelekinesisGrab();
            return true;
        }

        public void Dispose()
        {
            _vrPlayerService.TelekinesisDeactivated -= OnTelekinesisEnded;
        }

        private void OnTelekinesisEnded()
        {
            Logger.Log("[VRTelekinesisSpell] Telekinesis ended - stopping SFX", LogCat.VR);
            _fx.StopInvestSound();
            _vrPlayerService.TelekinesisDeactivated -= OnTelekinesisEnded;
        }
    }
}
