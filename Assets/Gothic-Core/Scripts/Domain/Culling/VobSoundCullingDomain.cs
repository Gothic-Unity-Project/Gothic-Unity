using System;
using UnityEngine;
using ZenKit.Vobs;

namespace Gothic.Core.Domain.Culling
{
    public class VobSoundCullingDomain : AbstractCullingDomain
    {
        private const int _initialCapacity = 64;

        // Shared by reference with the CullingGroup. Grows by doubling. The used entry amount is communicated
        // via SetBoundingSphereCount() - i.e. add/remove won't reallocate (and copy) the array each time.
        private BoundingSphere[] _spheres = new BoundingSphere[_initialCapacity];
        private GameObject[] _objects = new GameObject[_initialCapacity];
        private int _count;


        public override void PreWorldCreate()
        {
            base.PreWorldCreate();

            _spheres = new BoundingSphere[_initialCapacity];
            _objects = new GameObject[_initialCapacity];
            _count = 0;
        }

        /// <summary>
        ///Logic:
        /// 1. If In World loading state, we add all entries to the list based on rootVob position (e.g., a soundVob directly below levelCompo)
        /// 2. If After Loading, then added entries are subVobs (e.g., Cauldron->Sound) and we enlarge the cullingArray now.
        /// </summary>
        public void AddCullingEntry(GameObject go, ISound vob)
        {
            if (_count == _spheres.Length)
            {
                Array.Resize(ref _spheres, _count * 2);
                Array.Resize(ref _objects, _count * 2);

                if (CurrentState == State.WorldLoaded)
                    CullingGroup.SetBoundingSpheres(_spheres);
            }

            var index = _count++;
            _objects[index] = go;
            // FIXME - First call of VisibilityChanged() always provides visible=false? Is the pos+radius correct?
            _spheres[index] = new BoundingSphere(go.transform.position, vob.Radius / 100f); // Gothic's values are in cm, Unity's in m.

            // Sub-VOB sounds (e.g. Cauldron->Sound) are added after the world finished loading.
            if (CurrentState == State.WorldLoaded)
                CullingGroup.SetBoundingSphereCount(_count);
        }

        /// <summary>
        /// We only check for distance band 0 - visible, and 0 - invisible (or to be more precise here: audible/inaudible)
        /// </summary>
        protected override void VisibilityChanged(CullingGroupEvent evt)
        {
            // A higher distance level means "inaudible" as we only leverage: 0 -> in-range; 1 -> out-of-range.
            var inAudibleRange = evt.currentDistance == 0;
            var go = _objects[evt.index];

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
            CullingGroup.SetBoundingSpheres(_spheres);
            CullingGroup.SetBoundingSphereCount(_count);
            CullingGroup.onStateChanged = VisibilityChanged;
        }
    }
}
