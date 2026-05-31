using System;
using Gothic.Core.Adapters.Properties;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Proxy;
using Gothic.Core.Extensions;
using UnityEngine;
using ZenKit.Daedalus;

namespace Gothic.Core.Adapters.Npc
{
    public abstract class BasePlayerBehaviour : MonoBehaviour
    {
        [NonSerialized] public NpcContainer NpcData;

        public NpcInstance NpcInstance => NpcData.Instance;
        public NpcProxy Vob => NpcData.Vob;
        public GameObject Go => NpcData.Go;
        public NpcProperties Properties => NpcData.Props;
        public NpcPrefabProperties PrefabProps => NpcData.PrefabProps;

        protected virtual void Awake()
        {
            var lazyComp = GetComponentInParent<NpcLoader>();

            // As we lazy load NPCs, the NpcInstance is always set inside NpcLoader before we initialize this prefab!
            NpcData = lazyComp.Npc.GetUserData();
        }
    }
}
