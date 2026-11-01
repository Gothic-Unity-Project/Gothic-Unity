using Gothic.Core.Models.Config;
using UnityEngine;

namespace Gothic.Core.Services.Context
{
    public interface IContextInteractionService
    {
        string GetContextName();
        float GetFrameRate();
        void SetupPlayerController(DeveloperConfig developerConfig);
        GameObject GetCurrentPlayerController();
        void LockPlayerInPlace();

        /// <summary>
        /// Walking and teleporting off, turning untouched (e.g. a dialog: the player can still face the speaker).
        /// UnlockPlayer() ends it.
        /// </summary>
        void LockPlayerWalking();
        void UnlockPlayer();
        void SetWalkingControls();
        void SetSwimmingControls();
        void SetDivingControls();
        void TeleportPlayerTo(Vector3 position, Quaternion rotation = default);

        /// <summary>
        /// Holds the player horizontally inside the circle around center, measured at the head (the camera), so a
        /// physical step counts and turning doesn't. Between softRadius and radius, movement slows down the farther
        /// out the player is; walking outward stops at radius, walking back in speeds up steadily. Beyond radius the
        /// player drifts back.
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
