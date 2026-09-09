using System;
using System.Collections.Generic;

namespace Gothic.Testing.Recording
{
    /// <summary>
    /// Contents of >manifest.json<: everything needed to tell what produced an artifact bundle, and everything
    /// needed to reproduce it. (ADR-0001 §3.7)
    ///
    /// Serialized with Unity's own JsonUtility, so the fields are public and the class carries no logic.
    /// </summary>
    [Serializable]
    public class RunManifestData
    {
        public string RunId;
        public string StartedUtc;

        public string GitSha;
        public string UnityVersion;
        public string ApplicationVersion;

        public string Platform;
        public string Machine;
        public string OperatingSystem;
        public int ProcessorCount;
        public int SystemMemorySize;
        public string GraphicsDevice;

        /// <summary>
        /// One entry per scenario session in this run - a run is several scenarios, each with its own seed.
        /// </summary>
        public List<SessionManifestEntry> Sessions = new();
    }
}
