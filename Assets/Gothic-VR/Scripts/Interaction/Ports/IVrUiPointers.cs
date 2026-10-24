using Gothic.VR.Interaction.Models;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Gothic.VR.Interaction.Ports
{
    /// <summary>
    /// The UI laser pointers of both controllers. They only hit registered canvases.
    /// </summary>
    public interface IVrUiPointers
    {
        void RegisterCanvas(Canvas canvas);
        void UnregisterCanvas(Canvas canvas);

        /// <summary>
        /// Registers every canvas of all loaded scenes (also inactive ones). Slow, call it on scene changes only.
        /// </summary>
        void RegisterAllCanvases();

        /// <summary>
        /// Which hand's pointer raised the UI event. false if it came from no VR pointer.
        /// </summary>
        bool TryGetHandSide(PointerEventData eventData, out VrHandSide side);
    }
}
