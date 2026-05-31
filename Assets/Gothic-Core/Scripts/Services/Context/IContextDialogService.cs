using System.Collections.Generic;
using Gothic.Core.Models.Dialog;
using UnityEngine;
using ZenKit.Daedalus;

namespace Gothic.Core.Services.Context
{
    public interface IContextDialogService
    {
        public void StartDialogInitially();
        public void EndDialog();
        public void ShowDialog(GameObject npcGo);
        public void HideDialog();
        public void FillDialog(NpcInstance instance, List<DialogOption> dialogOptions);
        public void FillDialog(NpcInstance instance, List<InfoInstance> dialogOptions);
    }
}
