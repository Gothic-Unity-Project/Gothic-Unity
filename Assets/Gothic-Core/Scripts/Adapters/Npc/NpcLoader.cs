using Gothic.Core.Models.Container;
using Gothic.Core.Extensions;
using UnityEngine;
using ZenKit.Daedalus;

namespace Gothic.Core.Adapters.Npc
{
    public class NpcLoader : MonoBehaviour
    {
        public NpcInstance Npc;
        public NpcContainer Container => Npc.GetUserData();
        public bool IsLoaded;
    }
}
