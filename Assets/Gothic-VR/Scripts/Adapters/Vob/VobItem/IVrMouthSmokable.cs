using UnityEngine;

namespace Gothic.VR.Adapters.Vob.VobItem
{
    /// <summary>
    /// Something smoked at the VR mouth without being used up, e.g. a water pipe's mouthpiece (VRWaterPipe).
    /// VRMouthSmoking finds it above the collider that touches the mouth, tells it when that part comes and goes, and
    /// after a drag plays ExhaleSound and blows out the smoke of ExhaleAnimation - like a joint, but nothing is eaten
    /// and no script runs.
    /// </summary>
    public interface IVrMouthSmokable
    {
        /// <summary>
        /// The Humans.mds animation whose first particle effect is the smoke blown out (S_SMOKE_S1: LIGHTSMOKE).
        /// </summary>
        string ExhaleAnimation { get; }

        /// <summary>
        /// The sound (SfxInst) of breathing out the smoke, e.g. SMOKE_JOINT.
        /// </summary>
        string ExhaleSound { get; }

        /// <summary>
        /// The part touched the mouth. true: it is smoked now (e.g. the mouthpiece is in a hand), the mouth blows out
        /// its smoke. false: not now (lying around, another collider of the same object).
        /// </summary>
        bool TryStartSmoking(Collider part);

        /// <summary>
        /// The part smoked since TryStartSmoking left the mouth.
        /// </summary>
        void StopSmoking(Collider part);
    }
}
