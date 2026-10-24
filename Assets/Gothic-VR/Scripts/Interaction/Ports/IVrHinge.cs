using UnityEngine;

namespace Gothic.VR.Interaction.Ports
{
    /// <summary>
    /// A physically rotating door or chest lid.
    /// </summary>
    public interface IVrHinge
    {
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
