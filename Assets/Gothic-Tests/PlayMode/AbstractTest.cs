using System;
using System.Collections;
using GUZ.Core.Const;
using GUZ.Core.Models.Config;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;

namespace GUZ.Tests.PlayMode
{
    public abstract class AbstractTest
    {
        protected Scene MainScene => SceneManager.GetActiveScene();

        // FIXME - Needs to be VRPlayer or FlatPlayer.
        protected Scene GeneralScene => SceneManager.GetSceneByName(Constants.ScenePlayer);
        
        private readonly InputTestFixture _inputSimulator = new ();
        protected Keyboard Keyboard { get; private set; }
        protected Mouse Mouse { get; private set; }

        /// <summary>
        /// Unity seems to cache loading of Scriptable Objects. We therefore load it before entering Bootstrap scene and updating it.
        /// Bootstrap.GameManager.Config will then leverage the same _altered_ GameConfiguration we load in here.
        /// </summary>
        protected DeveloperConfig GetConfiguration()
        {
            return Resources.Load<DeveloperConfig>("GameConfigurations/Production");
        }

        protected IEnumerator PrepareTest()
        {
            _inputSimulator.Setup();
            Keyboard = InputSystem.AddDevice<Keyboard>();
            Mouse = InputSystem.AddDevice<Mouse>();
            
            SceneManager.LoadScene($"Assets/UnZENity-Core/Scenes/{Constants.SceneBootstrap}.unity");

            // Wait for 1 frame to successfully load Bootstrap scene.
            yield return null;
        }

        protected IEnumerator WaitForSceneLoaded(string sceneName)
        {
            var timeout = 30f;
            while (!SceneManager.GetSceneByName(sceneName).IsValid())
            {
                timeout -= Time.deltaTime;
                if (timeout < 0)
                {
                    throw new TimeoutException("Loading the scene took to long. Are you stuck in a wrong scene?");
                }

                yield return null;
            }

            // It's always a good idea to wait at least one additional frame. ;-)
            yield return null;
        }
        
        protected IEnumerator PressButton(KeyControl key, float time)
        {
            _inputSimulator.Press(key, time);
            yield return new WaitForSeconds(time);
        }
    }
}
