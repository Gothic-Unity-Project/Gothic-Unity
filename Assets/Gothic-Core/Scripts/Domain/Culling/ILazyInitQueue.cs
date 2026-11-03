using UnityEngine;

namespace Gothic.Core.Domain.Culling
{
    /// <summary>
    /// Something that builds queued objects (VOBs, NPCs) nearest first. A loading screen can restrict it to the
    /// surroundings of a point and ask how much of them is still missing.
    /// </summary>
    public interface ILazyInitQueue
    {
        /// <summary>
        /// Only objects within radius of center are initialized until ClearInitFocus(), e.g. while the view is black
        /// after loading. The others wait, so the near ones get the whole budget.
        /// </summary>
        void SetInitFocus(Vector3 center, float radius);

        void ClearInitFocus();

        /// <summary>
        /// How many objects within radius of center still wait for their lazy initialization.
        /// </summary>
        int CountQueuedWithin(Vector3 center, float radius);
    }
}
