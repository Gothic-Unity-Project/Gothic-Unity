using Gothic.Core.Manager;
using Gothic.Core.Models.Container;
using Gothic.Core.Services;
using Gothic.Tests.Behaviour.Driver;
using Gothic.Tests.Behaviour.Runner;
using Gothic.Tests.Behaviour.Session;
using NUnit.Framework;
using Reflex.Attributes;

namespace Gothic.Tests.Behaviour.Steps.Dialog
{
    /// <summary>
    /// Dialogs between an NPC and the hero, started the way the scripts' AI_ProcessInfos ends
    /// (DialogService.StartDialog) and checked by the dialog state (GameStateService.Dialogs).
    /// </summary>
    public class DialogSteps
    {
        [Inject] private readonly DialogService _dialogService;
        [Inject] private readonly GameStateService _gameStateService;

        private readonly ScenarioScope _scope;


        public DialogSteps(ScenarioScope scope)
        {
            _scope = scope;
        }

        [Step("\"(.*)\" starts a dialog with the hero")]
        public void StartsADialogWithTheHero(string instance)
        {
            var npc = _scope.Driver<NpcDriver>().Find(instance) ??
                      throw new AssertionException($"{instance} isn't in the world");

            _dialogService.StartDialog(npc, true);
            _scope.Undo($"end the dialog with {instance}", () => EndDialogWith(npc));
        }

        [Step("a dialog is (open|not open)")]
        public void ADialogIs(string state)
        {
            var dialogs = _gameStateService.Dialogs;
            var actual = dialogs.IsInDialog ? "open" : "not open";
            Assert.That(actual, Is.EqualTo(state),
                $"A dialog is {actual}{(dialogs.IsInDialog ? $" with {dialogs.CurrentDialogNpc?.Go?.name}" : "")}");
        }

        private void EndDialogWith(NpcContainer npc)
        {
            var dialogs = _gameStateService.Dialogs;
            if (dialogs.IsInDialog && dialogs.CurrentDialogNpc == npc)
                _dialogService.StopDialog(npc);
        }
    }
}
