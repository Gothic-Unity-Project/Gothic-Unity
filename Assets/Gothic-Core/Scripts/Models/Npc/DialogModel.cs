using System.Collections.Generic;
using Gothic.Core.Models.Dialog;
using ZenKit;
using ZenKit.Daedalus;

namespace Gothic.Core.Models.Npc
{
    public class DialogModel
    {
        public List<InfoInstance> Instances = new();
        public bool IsInDialog;

        public CutsceneLibrary CutsceneLibrary;

        public int GestureCount;

        public InfoInstance CurrentInstance;
        public List<DialogOption> CurrentOptions = new();
        
        public void Dispose()
        {
            IsInDialog = false;
            CurrentInstance = null;
            CurrentOptions.Clear();
            GestureCount = 0;
        }
    }
}
