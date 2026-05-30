#if GUZ_HVR_INSTALLED
using System.Collections.Generic;
using System.Linq;
using GUZ.Core.Const;
using GUZ.Core.Models.Dialog;
using GUZ.Core.Services.Context;
using GUZ.VR.Adapters.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using ZenKit.Daedalus;

namespace GUZ.VR.Services.Context
{
    public class VRContextDialogService : IContextDialogService
    {
        private VRDialog _dialogComponent;

        private VRDialog GetDialog()
        {
            // The component is stored in General scene. We therefore load it when accessing for the first time.
            if (_dialogComponent == null)
            {
                var scene = SceneManager.GetSceneByName(Constants.ScenePlayer);

                // Try a second time with main scene. Used for Lab.
                if (!scene.IsValid())
                {
                    scene = SceneManager.GetActiveScene();
                }
                
                // We need to look through all RootGOs and fetch the first matching HVRDialog Component.
                _dialogComponent = scene.GetRootGameObjects()
                    .Select(i => i.GetComponentInChildren<VRDialog>(true))
                    .First(i => i != null);
            }

            return _dialogComponent;
        }

        public void StartDialogInitially()
        {
            GetDialog().StartDialogInitially();
        }

        public void EndDialog()
        {
            GetDialog().EndDialog();
        }

        public void ShowDialog(GameObject npcGo)
        {
            var dialog = GetDialog();
            dialog.ShowDialog(npcGo);
        }

        public void HideDialog()
        {
            GetDialog().HideDialog();
        }

        public void FillDialog(NpcInstance instance, List<DialogOption> dialogOptions)
        {
            GetDialog().FillDialog(instance, dialogOptions);
        }

        public void FillDialog(NpcInstance instance, List<InfoInstance> dialogOptions)
        {
            GetDialog().FillDialog(instance, dialogOptions);
        }
    }
}
#endif
