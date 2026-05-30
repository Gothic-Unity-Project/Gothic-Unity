using GUZ.Core.Models.Container;
using GUZ.Core.Extensions;
using UnityEngine;
using ZenKit.Daedalus;

namespace GUZ.Core.Adapters.Npc
{
    public class NpcLoader : MonoBehaviour
    {
        public NpcInstance Npc;
        public NpcContainer Container => Npc.GetUserData();
        public bool IsLoaded;
    }
}
