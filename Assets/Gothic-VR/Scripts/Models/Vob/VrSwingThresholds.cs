namespace Gothic.VR.Models.Vob
{
    /// <summary>
    /// When a held melee weapon's movement is a swing (VrSwingSampler). Speeds in m/s, times in s, paths in m - all in
    /// the rig's tracking space, so walking, running, turning and teleports don't count.
    /// </summary>
    public struct VrSwingThresholds
    {
        public float HandleSpeed;
        public float TipSpeed;
        public float ThrustSpeed;
        public float MinTimeOver;
        public float MinTipPath;
        public float PathWindow;
    }
}
