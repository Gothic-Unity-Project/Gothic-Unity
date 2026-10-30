using Gothic.Core.Models.Config;
using Gothic.Core.Models.Context;
using UnityEngine;

namespace Gothic.Core.Services.Context
{
    public interface IContextInteractionService
    {
        string GetContextName();
        float GetFrameRate();
        void SetupPlayerController(DeveloperConfig developerConfig);
        GameObject GetCurrentPlayerController();
        /// <summary>
        /// Blocks these player controls, the others are free. ContextInteractionService combines the locks of all
        /// owners (LockPlayer(), UnlockPlayer()) and passes the result on.
        /// </summary>
        void SetBlockedControls(PlayerControls blockedControls);
        void SetWalkingControls();
        void SetSwimmingControls();
        void SetDivingControls();
        void TeleportPlayerTo(Vector3 position, Quaternion rotation = default);

        /// <summary>
        /// Holds the player horizontally inside the circle around center. Between softRadius and radius, movement
        /// slows down the farther out the player is; walking outward stops at radius, walking back in speeds up
        /// steadily. Beyond radius the player drifts back.
        /// Call it every frame while holding. Does nothing while the player is airborne or climbing.
        /// </summary>
        void KeepPlayerWithin(Vector3 center, float softRadius, float radius);
        void InitUIInteraction();
        void IntroduceChapter(string chapter, string text, string texture, string wav, int time);
        void DisableMenus();
        void EnableMenus();
        void SetWaterWalkingControls();
    }
}
