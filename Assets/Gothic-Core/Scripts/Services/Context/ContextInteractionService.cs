using GUZ.Core.Models.Config;
using UnityEngine;

namespace GUZ.Core.Services.Context
{
    public class ContextInteractionService : IContextInteractionService
    {
        private IContextInteractionService _impl;

        public void SetImpl(IContextInteractionService proxy)
        {
            _impl = proxy;
        }

        public T GetImpl<T>() where T : IContextInteractionService
        {
            return (T)_impl;
        }

        public string GetContextName()
        {
            return _impl.GetContextName();
        }

        public float GetFrameRate()
        {
            return _impl.GetFrameRate();
        }

        public void SetupPlayerController(DeveloperConfig developerConfig)
        {
            _impl.SetupPlayerController(developerConfig);
        }

        public GameObject GetCurrentPlayerController()
        {
            return _impl.GetCurrentPlayerController();
        }

        public void LockPlayerInPlace()
        {
            _impl.LockPlayerInPlace();
        }

        public void UnlockPlayer()
        {
            _impl.UnlockPlayer();
        }

        public void SetWalkingControls()
        {
            _impl.SetWalkingControls();
        }

        public void SetWaterWalkingControls()
        {
            _impl.SetWaterWalkingControls();
        }

        public void SetSwimmingControls()
        {
            _impl.SetSwimmingControls();
        }

        public void SetDivingControls()
        {
            _impl.SetDivingControls();
        }

        public void TeleportPlayerTo(Vector3 position, Quaternion rotation = default)
        {
            _impl.TeleportPlayerTo(position,rotation);
        }

        public void InitUIInteraction()
        {
            _impl.InitUIInteraction();
        }

        public void IntroduceChapter(string chapter, string text, string texture, string wav, int time)
        {
            _impl.IntroduceChapter(chapter, text, texture, wav, time);
        }

        public void DisableMenus()
        {
            _impl.DisableMenus();
        }

        public void EnableMenus()
        {
            _impl.EnableMenus();
        }
    }
}
