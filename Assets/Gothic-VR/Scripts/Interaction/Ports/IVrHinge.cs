using UnityEngine;

namespace Gothic.VR.Interaction.Ports
{
    /// <summary>
    /// A physically rotating door or chest lid.
    /// Shared prefabs may carry one hinge per backend: use the active one (VrHinges.FindActive).
    /// </summary>
    public interface IVrHinge
    {
        /// <summary>
        /// Whether this hinge belongs to the active VR backend.
        /// </summary>
        bool IsActive { get; }

        Transform Transform { get; }
        Rigidbody Rigidbody { get; }

        /// <summary>
        /// Stops the hinge from rotating (locked door/chest).
        /// </summary>
        void Lock();

        void Unlock();

        /// <summary>
        /// Sounds played by the hinge when it reaches its opened or closed position.
        /// </summary>
        void SetSounds(AudioClip opened, AudioClip closed);
    }
}
