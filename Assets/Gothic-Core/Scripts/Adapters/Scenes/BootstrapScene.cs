using System.Collections;
using Gothic.Core.Const;
using Gothic.Core.Services;
using Gothic.Core.Services.Config;
using Gothic.Core.Manager;
using Reflex.Attributes;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Gothic.Core.Adapters.Scenes
{
    /// <summary>
    /// Specific manager for Bootstrap.unity scene tasks only.
    /// </summary>
    public class BootstrapScene : MonoBehaviour, IScene
    {
        [Inject] private readonly ConfigService _configService;
        [Inject] private readonly BootstrapService _bootstrapService;


        public void Init()
        {
            StartCoroutine(BootUnity());
        }

        private IEnumerator BootUnity()
        {
            var bootstrapScene = SceneManager.GetSceneByName(Constants.SceneBootstrap);
            
            // Check if other scenes are active and remove them now (We need to have a clean Bootstrap scene at start time only).
            while (SceneManager.loadedSceneCount != 1)
            {
                var nextSceneIndex = SceneManager.GetSceneAt(0) == bootstrapScene ? 1 : 0;
                var nextScene = SceneManager.GetSceneAt(nextSceneIndex);
                SceneManager.UnloadSceneAsync(nextScene);
                yield return null;
            }

            // basically Flat or VR
            var playerContextName = _configService.Dev.GameControls.ToString();

            // Load Player scene by its full path name. Otherwise, it will not be found as Flat and VR module have same Player.unity scene name in use.
            _bootstrapService.LoadScene($"Gothic-{playerContextName}/Scenes/{playerContextName}/{Constants.ScenePlayer}");
        }
    }
}
