using Newtonsoft.Json.Linq;

namespace Gothic.Core.Services.World
{
    /// <summary>
    /// State which Gothic's save format doesn't store (or ZenKit can't write correctly) goes into UNITYSAVE.json.
    /// Core's sections live in Domain/SaveGames and are registered in SaveGameService.Init(). Modules (VR, Flat,
    /// G1/G2, mods) register their own via SaveGameService.RegisterSection(), Core doesn't know them.
    /// </summary>
    public interface ISaveStateSection
    {
        /// <summary>
        /// Unique key, prefixed by its owner: "core.*", "vr.*", "flat.*" or "{modId}.*".
        /// </summary>
        string Key { get; }

        /// <summary>
        /// Increase whenever the data format changes. Restore() gets the version which was saved.
        /// </summary>
        int Version { get; }

        JToken Capture();

        /// <summary>
        /// Called when a new game starts and right before Restore() when a save game is loaded (also if the save game
        /// has no data for this section): forget the state of the previous game.
        /// </summary>
        void Reset();

        /// <summary>
        /// Called when a save game is loaded, before its first world is created. Sections which need the world
        /// keep the data and apply it later (e.g. on GlobalEventDispatcher.WorldSceneLoaded).
        /// </summary>
        void Restore(JToken data, int savedVersion);
    }
}
