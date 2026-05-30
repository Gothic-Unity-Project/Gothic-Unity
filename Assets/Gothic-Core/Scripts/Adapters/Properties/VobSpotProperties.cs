using GUZ.Core.Models.Vob.WayNet;
using UnityEngine;

namespace GUZ.Core.Adapters.Properties
{
    public class VobSpotProperties : VobProperties
    {
        public FreePoint Fp;
        
        [SerializeField] private bool _isLocked;

        // Called every frame when selected. OnValidate() wouldn't work, as Fp itself isn't changing. Just it's children.
        private void OnDrawGizmosSelected()
        {
            _isLocked = Fp.IsLocked;
        }
    }
}
