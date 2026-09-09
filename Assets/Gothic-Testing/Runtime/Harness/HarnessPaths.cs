using System.IO;
using UnityEngine;

namespace Gothic.Testing.Harness
{
    /// <summary>
    /// The two root directories every other harness class derives its paths from.
    ///
    /// Kept in one place because the Editor lane and the standalone-player lane disagree about them, and that
    /// disagreement should be settled once rather than in every recorder. (ADR-0001 D1)
    /// </summary>
    public static class HarnessPaths
    {
        /// <summary>
        /// The Unity project root - i.e. the folder holding Assets/, Packages/ and .git/.
        ///
        /// A standalone player has no project root; Application.dataPath is the only thing which exists there and
        /// it points into the build, so callers get a path which exists but carries no .git. That is intentional:
        /// every caller has a fallback for a missing repository anyway.
        /// </summary>
        public static string ProjectRoot =>
            Application.isEditor
                ? Directory.GetParent(Application.dataPath).FullName
                : Application.dataPath;

        /// <summary>
        /// Where run artifacts are written. Inside the project in the Editor, so a developer finds them next to
        /// the code; in the writable persistent path for a player, where the install directory may be read-only.
        /// </summary>
        public static string ArtifactRoot =>
            Application.isEditor
                ? Path.Combine(ProjectRoot, "Artifacts", "functional")
                : Path.Combine(Application.persistentDataPath, "Artifacts", "functional");
    }
}
