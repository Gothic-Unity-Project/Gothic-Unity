#if GOTHIC_HVR_INSTALLED
namespace Gothic.VR.Interaction.Backends.Hvr
{
    /// <summary>
    /// Shoulder holster with the same VobLoader re-parenting as HvrSocket.
    /// HvrSocketBridge.IsShoulder detects it (VRBackpack refreshes its inventory when it's taken from a shoulder).
    /// </summary>
    public class HvrShoulderSocket : HvrSocket
    {
    }
}
#endif
