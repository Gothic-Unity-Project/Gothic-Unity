using Gothic.Core.Models.Container;
using UnityEngine;

namespace Gothic.Core.Models.Vob
{
    /// <summary>
    /// A torch while it burns (TorchService): its flame (with the burning sound), the normal light it gives the world
    /// and the glow around the flame, which only shows while the torch is the hero light.
    /// </summary>
    public class BurningTorch
    {
        public readonly VobContainer Torch;
        public readonly GameObject Fire;
        public readonly GameObject Light;
        /// <summary>
        /// Made the first time the torch is the hero light.
        /// </summary>
        public GameObject Glow;

        public BurningTorch(VobContainer torch, GameObject fire, GameObject light)
        {
            Torch = torch;
            Fire = fire;
            Light = light;
        }
    }
}
