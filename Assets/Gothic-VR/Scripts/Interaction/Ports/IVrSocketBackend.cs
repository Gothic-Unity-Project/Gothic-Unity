using Gothic.VR.Interaction.Components;

namespace Gothic.VR.Interaction.Ports
{
    /// <summary>
    /// Implemented by the backend part of a VrSocketSlot (e.g. XriSocketBridge) on the same GameObject. It detects
    /// interactables (hover, release into the slot) and holds them in the backend's terms. The Gothic part
    /// (re-parenting the VobLoader, rotation, scale, sounds, events) stays in VrSocketSlot. The backend reports to
    /// the slot via VrSocketSlot.OnBackend*().
    /// Game code uses VrSocketSlot (IVrSocket) instead.
    /// </summary>
    public interface IVrSocketBackend
    {
        /// <summary>
        /// Whether this bridge belongs to the active VR backend.
        /// </summary>
        bool IsActive { get; }

        /// <summary>
        /// Selects the interactable in the backend, ignoring the filters. The slot has already attached it and
        /// raised its events. May finish later (e.g. while the interactable isn't registered at the backend yet).
        /// </summary>
        bool TryPlace(VrInteractable interactable);

        /// <summary>
        /// Releases the interactable (or drops a placement which hasn't finished yet). The backend reports the
        /// release via VrSocketSlot.OnBackendRemoved, if it had selected the interactable.
        /// </summary>
        void ForceRelease();

        /// <summary>
        /// Off: the slot neither detects nor accepts interactables (e.g. backpack slots while the backpack is on the
        /// shoulder). A held interactable stays.
        /// </summary>
        void SetDetectionEnabled(bool isEnabled);
    }
}
