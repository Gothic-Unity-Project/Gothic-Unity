using Gothic.Core.Const;

namespace Gothic.Core.Models.Movement
{
    /// <summary>
    /// The climber's capsule and what it collides with. Used for the room and headroom checks of a ledge probe.
    /// </summary>
    public struct LedgeBody
    {
        public float Radius;
        public float Height;

        /// <summary>
        /// Layers the climber collides with (without its own layer). NPCs and items block room and headroom.
        /// </summary>
        public int LayerMask;

        /// <summary>
        /// Layers a ledge can be made of: like OpenGothic's ray (landscape and objects), no NPCs or loose items.
        /// </summary>
        public int SurfaceLayerMask;

        public LedgeBody(float radius, float height, int layerMask)
        {
            Radius = radius;
            Height = height;
            LayerMask = layerMask;
            SurfaceLayerMask = layerMask & ~((1 << Constants.VobNpcOrMonsterLayer) | (1 << Constants.DynamicPoseLayer) |
                                             (1 << Constants.VobItemLayer) |
                                             (1 << Constants.VobItemNoWorldCollision));
        }
    }
}
