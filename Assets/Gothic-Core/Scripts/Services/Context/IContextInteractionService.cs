using GUZ.Core.Models.Config;
using UnityEngine;

namespace GUZ.Core.Services.Context
{
    public interface IContextInteractionService
    {
        string GetContextName();
        float GetFrameRate();
        void SetupPlayerController(DeveloperConfig developerConfig);
        GameObject GetCurrentPlayerController();
        void LockPlayerInPlace();
        void UnlockPlayer();
        void SetWalkingControls();
        void SetSwimmingControls();
        void SetDivingControls();
        void TeleportPlayerTo(Vector3 position, Quaternion rotation = default);
        void InitUIInteraction();
        void IntroduceChapter(string chapter, string text, string texture, string wav, int time);
        void DisableMenus();
        void EnableMenus();
        void SetWaterWalkingControls();
    }
}
