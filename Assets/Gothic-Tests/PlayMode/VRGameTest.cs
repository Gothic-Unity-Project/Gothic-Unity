using System;
using System.Collections;
using System.Linq;
using GUZ.Core;
using GUZ.Core.Adapters;
using GUZ.Core.Const;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace GUZ.Tests.PlayMode
{
    /// <summary>
    /// As loading our game takes about 30 seconds, we will leverage this class like this:
    /// 1. Load game once only.
    /// 2. Every test will be in order and execute/test another part of the game.
    /// Keep in mind: We won't restart game within each test, but reuse existing game session.
    /// </summary>
    public class VRGameTest : AbstractTest
    {
        private bool _alreadySetUp;

        [UnitySetUp]
        private IEnumerator SetUp()
        {
            // Only UnitySetup will act as Coroutine for setup, but it is called every Test. Therefore, make it single-run here.
            if (_alreadySetUp)
            {
                yield break;
            }
            _alreadySetUp = true;

            // Arrange
            var config = GetConfiguration();
            config.EnableVRDeviceSimulator = true;
            config.EnableMainMenu = false;

            // Act
            yield return PrepareTest();

            // Assert
            if (SceneManager.GetActiveScene().GetRootGameObjects().First().GetComponent<BootstrapAdapter>().DeveloperConfig.name != "Production")
            {
                // Unfortunately I couldn't figure out an easy way to check for active .Config setting without triggering .Start() of GameObject.
                // Therefore let's check that we always have Production active.
                throw new InvalidProgramException(
                    ">Bootstrap.GameManager.Config< needs to be set to >GameConfigurations/Production< for tests to work properly.");
            }

            // Act - Start and Wait for game to run properly.
            EditorApplication.EnterPlaymode();
            yield return WaitForSceneLoaded(Constants.ScenePlayer);
        }
        
        
        [UnityTest]
        public IEnumerator GameLoadedTest()
        {
            // Assert
            Assert.That(Camera.main != null, "There is no main camera. Game might not be loaded correctly.");
            yield break;
        }

        [UnityTest]
        public IEnumerator TestGameplay()
        {
            // Arrange
            var playerGo = GameObject.FindWithTag(Constants.PlayerTag);
            var initialPos = playerGo.transform.position;

            // Act
            yield return PressButton(Keyboard.wKey, 3f);
            
            // Assert
            var newPos = playerGo.transform.position;
            
            Assert.That(Math.Abs(initialPos.x - newPos.x) > 0.5f, "Player didn't move.");
        }
    }
}
