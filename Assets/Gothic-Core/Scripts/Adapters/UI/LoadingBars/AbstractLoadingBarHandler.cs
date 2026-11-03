using System.Collections.Generic;
using Gothic.Core.Services.Meshes;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.UI;

namespace Gothic.Core.Adapters.UI.LoadingBars
{
    public abstract class AbstractLoadingBarHandler : MonoBehaviour
    {
        public Image LoadingImage;
        public Image ProgressBackgroundImage;
        public Image ProgressBarImage;

        
        [Inject] protected readonly TextureService TextureService;

        private Sprite _barSprite;

        public abstract List<string> GetProgressTypes();
        
        protected virtual void Start()
        {
            SetMaterials();
        }
        
        /// <summary>
        /// While loading, the player stands at this handler's position, looking along +z (LoadingScene). When they are
        /// put somewhere else before the loading is over, the picture and bar move along and stay in front of them,
        /// e.g. at the spawn point while the world around them is built. The floor stays here.
        /// </summary>
        public void MoveAlongWithPlayer(Vector3 playerPosition, Quaternion playerRotation)
        {
            // Only the turn around the up axis: the screen stays upright. No rotation given = looking along +z.
            var turn = playerRotation == default
                ? Quaternion.identity
                : Quaternion.Euler(0f, playerRotation.eulerAngles.y, 0f);

            var screen = LoadingImage.canvas.transform;
            screen.SetPositionAndRotation(playerPosition + turn * (screen.position - transform.position),
                turn * screen.rotation);
        }

        protected virtual void SetMaterials()
        {
            LoadingImage.material = TextureService.GothicLoadingMenuMaterial;
            ProgressBackgroundImage.material = TextureService.LoadingBarBackgroundMaterial;
            ProgressBarImage.material = TextureService.LoadingBarMaterial;

            // The bar only fills with a sprite. Its own picture instead of the placeholder sprite: shaders which take
            // the picture from the sprite (VR's on-top UI on the black view) draw the bar, not a white box.
            if (TextureService.LoadingBarMaterial.mainTexture is Texture2D barTexture)
            {
                _barSprite = Sprite.Create(barTexture, new Rect(0, 0, barTexture.width, barTexture.height),
                    new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                ProgressBarImage.sprite = _barSprite;
            }
        }

        protected virtual void OnDestroy()
        {
            if (_barSprite != null)
                Destroy(_barSprite);
        }
    }
}
