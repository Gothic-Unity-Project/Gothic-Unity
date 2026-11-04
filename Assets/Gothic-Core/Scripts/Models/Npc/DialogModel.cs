using System.Collections.Generic;
using Gothic.Core.Models.Container;
using Gothic.Core.Models.Dialog;
using ZenKit;
using ZenKit.Daedalus;

namespace Gothic.Core.Models.Npc
{
    public class DialogModel
    {
        public List<InfoInstance> Instances = new();
        public bool IsInDialog;
        public NpcContainer CurrentDialogNpc;

        /// <summary>
        /// Set from C# when the hero physically initiates dialog (collision/grab).
        /// False when the NPC initiates (e.g. important dialog walks to player).
        /// Used to suppress hero's reactive "hey" SVM lines for NPC-initiated conversations.
        /// </summary>
        public bool WasPlayerInitiated;

        public CutsceneLibrary CutsceneLibrary;

        public int GestureCount;

        public InfoInstance CurrentInstance;
        public List<DialogOption> CurrentOptions = new();

        /// <summary>
        /// The dialog box shows the choices - nobody talks.
        /// </summary>
        public bool IsChoosing;

        /// <summary>
        /// The END entry (DIALOG_ENDE) of the shown main choices. Null for sub-choices (Info_AddChoice) and choices
        /// without END - like in Gothic, those can't be left.
        /// </summary>
        public InfoInstance ExitInfo;

        public void Dispose()
        {
            IsInDialog = false;
            WasPlayerInitiated = false;
            CurrentInstance = null;
            CurrentOptions.Clear();
            GestureCount = 0;
            CurrentDialogNpc = null;
            IsChoosing = false;
            ExitInfo = null;
        }
    }
}
