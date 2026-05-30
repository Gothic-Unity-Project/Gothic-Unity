using UnityEngine;
using ZenKit.Vobs;

namespace GUZ.Core.Domain.Culling
{
    public class VobSoundCullingDomain : AbstractCullingDomain
    {
        public void AddCullingEntry(GameObject go, ISound vob)
        {
            AddCullingEntryInternal(go, vob);
        }

        /// <summary>
        ///Logic:
        /// 1. If In World loading state, we add all entries to the list based on rootVob position (e.g., a soundVob directly below levelCompo)
        /// 2. If After Loading, then added entries are subVobs (e.g., Cauldron->Sound) and we enlarge the cullingArray now.
        /// </summary>
        private void AddCullingEntryInternal(GameObject go, ISound vob)
        {
            Objects.Add(go);
            
            // FIXME - First call of VisibilityChanged() always provides visible=false? Is the pos+radius correct?
            var sphere = new BoundingSphere(go.transform.position, vob.Radius / 100f); // Gothic's values are in cm, Unity's in m.
            Spheres.Add(sphere);

            if (CurrentState == State.WorldLoaded)
            {
                // Each time we add an entry, we need to recreate the array for the CullingGroup.
                CullingGroup.SetBoundingSpheres(Spheres.ToArray());
            }
        }
        
        /// <summary>
        /// We only check for distance band 0 - visible, and 0 - invisible (or to be more precise here: audible/inaudible)
        /// </summary>
        protected override void VisibilityChanged(CullingGroupEvent evt)
        {
            // A higher distance level means "inaudible" as we only leverage: 0 -> in-range; 1 -> out-of-range.
            var inAudibleRange = evt.currentDistance == 0;
            var go = Objects[evt.index];
            
            go.SetActive(inAudibleRange);

            if (inAudibleRange)
            {
                GlobalEventDispatcher.VobMeshCullingChanged.Invoke(go);
            }
        }

        /// <summary>
        /// Set main camera once world is loaded fully.
        /// Doesn't work at loading time as we change scenes etc.
        /// </summary>
        public override void PostWorldCreate()
        {
            base.PostWorldCreate();

            // Disable sounds if we're leaving the area and therefore last audible location.
            // Hint: As there are non-spatial sounds (always same volume wherever we are),
            // we need to disable the sounds at exactly the spot we are.
            CullingGroup.SetBoundingDistances(new[] { 0f });
            CullingGroup.SetBoundingSpheres(Spheres.ToArray());
            CullingGroup.onStateChanged = VisibilityChanged;
        }
    }
}
