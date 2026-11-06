using System.Collections.Generic;
using UnityEngine;

namespace Gothic.Core.Models.Container
{
    /// <summary>
    /// A fireplace/torch (oCMobFire) and the objects of its fire tree (flame, smoke, light, sound) - FireService
    /// switches them on and off.
    /// </summary>
    public class FireContainer
    {
        public GameObject Go;
        // Scheme of its mob routine (Wld_SetMobRoutine), e.g. FIREPLACE.
        public string Scheme;
        // Indoors (light mapped: houses, mines) it always burns.
        public bool IsAlwaysLit;
        public readonly List<GameObject> FireObjects = new();

        // Lit or put out by the hero, kept until the routine switches (ManualRoutineState).
        public bool? ManualLit;
        public int ManualRoutineState;
        // Whether the fire objects are on now - null before the first refresh.
        public bool? IsActive;
    }
}
