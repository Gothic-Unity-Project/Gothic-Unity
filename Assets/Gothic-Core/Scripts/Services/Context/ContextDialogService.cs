using System.Collections.Generic;
using GUZ.Core.Models.Dialog;
using UnityEngine;
using ZenKit.Daedalus;

namespace GUZ.Core.Services.Context
{
    public class ContextDialogService : IContextDialogService
    {
        private IContextDialogService _impl;

        public void SetImpl(IContextDialogService impl)
        {
            _impl = impl;
        }

        public T GetImpl<T>() where T : IContextDialogService
        {
            return (T)_impl;
        }

        public void StartDialogInitially()
        {
            _impl.StartDialogInitially();
        }

        public void EndDialog()
        {
            _impl.EndDialog();
        }

        public void ShowDialog(GameObject npcGo)
        {
            _impl.ShowDialog(npcGo);
        }

        public void HideDialog()
        {
            _impl.HideDialog();
        }

        public void FillDialog(NpcInstance instance, List<DialogOption> dialogOptions)
        {
            _impl.FillDialog(instance, dialogOptions);
        }

        public void FillDialog(NpcInstance instance, List<InfoInstance> dialogOptions)
        {
            _impl.FillDialog(instance, dialogOptions);
        }
    }
}
