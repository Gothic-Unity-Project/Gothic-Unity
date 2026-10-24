namespace Gothic.Core.Models.Context
{
    /// <summary>
    /// VR interaction framework which provides the VR ports (rig, input, haptics, UI pointers).
    /// Stored as int in DeveloperConfig assets. Don't change the values.
    /// </summary>
    public enum VrInteractionBackend
    {
        /// <summary>
        /// HurricaneVR (commercial). Needs the scripting define GOTHIC_HVR_INSTALLED.
        /// </summary>
        Hvr = 0,

        /// <summary>
        /// XR Interaction Toolkit (free, always installed).
        /// </summary>
        Xri = 1
    }
}
