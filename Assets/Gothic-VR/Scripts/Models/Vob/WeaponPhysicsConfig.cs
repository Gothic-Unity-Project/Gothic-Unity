namespace GUZ.VR.Models.Vob
{
    /// <summary>
    /// Share data between Adapter and Service/Domain layer.
    /// </summary>
    public struct WeaponPhysicsConfig
    {
        public float Mass2HOneHanded;
        public float Mass1HAnyHand2HTwoHanded;
        public float LinearDamping2HOneHanded;
        public float LinearDamping1HAnyHand2HTwoHanded;
        public float AngularDamping2HOneHanded;
        public float AngularDamping1HAnyHand2HTwoHanded;
        public float WeaponVelocityThreshold;
        public float WeaponVelocityDropPercentage;
        public float VelocityCheckDuration;
        public int VelocitySampleCount;
    }
}
