using ZenKit.Vobs;

namespace GUZ.Core.Adapters.Properties.Vobs
{
    public class InteractiveProperties : MovableProperties
    {
        /// <summary>
        /// Runtime state of an Interactable. e.g., Wheel is opened.
        /// This property isn't stored in G1 Save Games and will always start with 0 when VOB is initialized.
        /// </summary>
        public int State = 0;

        
        public InteractiveProperties(IVirtualObject vob) : base(vob)
        { }
    }
}
