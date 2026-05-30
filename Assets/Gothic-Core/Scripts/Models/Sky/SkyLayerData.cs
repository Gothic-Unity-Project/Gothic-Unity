using UnityEngine;

namespace GUZ.Core.Models.Sky
{
    public class SkyLayerData
    {
        public Texture2D[] TEXBox;
        public Texture2D TEX;
        public string TEXName = "";
        public float TEXAlpha;
        public float TEXScale = 1;
        public Vector2 TEXSpeed = new(0.9f, 1.1f);
    }
}
