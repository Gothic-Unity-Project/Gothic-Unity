using System.Collections;
using Gothic.Core.Services.World;
using Gothic.Tests.Behaviour.Runner;
using Gothic.Tests.Behaviour.Session;

namespace Gothic.Tests.Behaviour.Steps.SaveGames
{
    /// <summary>
    /// Saving and loading in the running game. Test slot 15 is backed up and restored by the session.
    /// </summary>
    public class SaveGameSteps
    {
        private const SaveGameService.SlotId _testSlot = SaveGameService.SlotId.Slot15;


        /// <summary>
        /// Tag the scenario @slow: it loads the world again (~10-30 s).
        /// </summary>
        [Step("the game is saved and loaded again")]
        public IEnumerator TheGameIsSavedAndLoadedAgain()
        {
            yield return GameSession.SaveAndLoad(_testSlot);
        }
    }
}
