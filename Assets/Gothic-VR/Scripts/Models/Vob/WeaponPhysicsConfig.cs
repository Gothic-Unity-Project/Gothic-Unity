namespace Gothic.VR.Models.Vob
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

        // Swing detection (VrSwingSampler), in tracking space. Speeds m/s, times s, paths m.
        public float HandleSpeed1H;
        public float TipSpeed1H;
        public float HandleSpeed2H;
        public float TipSpeed2H;
        public float ThrustSpeed;
        public float MinTimeOver;
        public float MinTipPath;
        public float PathWindow;
        public float WeaponVelocityDropPercentage;
    }
}
