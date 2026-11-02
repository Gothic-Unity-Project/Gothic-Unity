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
        /// <summary>
        /// No walking/teleporting. allowTurning: the player can still turn (snap/smooth turn) - knocked out heroes.
        /// </summary>
        void LockPlayerInPlace(bool allowTurning = false);
        void UnlockPlayer();
        void SetWalkingControls();
        void SetSwimmingControls();
        void SetDivingControls();
        void TeleportPlayerTo(Vector3 position, Quaternion rotation = default);

        /// <summary>
        /// What the hands hold goes into the hero's inventory (teleport, saving).
        /// </summary>
        void StoreHeldItemsInBackpack();

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
