using System;
using ZenKit;

namespace Gothic.Core.Domain.Vm
{
    /// <summary>
    /// Sets self/other/victim for one script call and restores the previous values on Dispose:
    /// using (new VmGlobalsScope(vm, self, other, victim)) vm.Call(function);
    /// Like the engine (OpenGothic invokeState), the given values are set as they are, null included - a left-over
    /// victim made B_AssessWarn or B_AssessUseMob see a crime that wasn't there.
    /// A struct, so a perception call per NPC and tick allocates nothing.
    /// </summary>
    public readonly struct VmGlobalsScope : IDisposable
    {
        private readonly DaedalusVm _vm;
        private readonly DaedalusInstance _oldSelf;
        private readonly DaedalusInstance _oldOther;
        private readonly DaedalusInstance _oldVictim;

        public VmGlobalsScope(DaedalusVm vm, DaedalusInstance self, DaedalusInstance other, DaedalusInstance victim)
        {
            _vm = vm;
            _oldSelf = vm.GlobalSelf;
            _oldOther = vm.GlobalOther;
            _oldVictim = vm.GlobalVictim;
            vm.GlobalSelf = self;
            vm.GlobalOther = other;
            vm.GlobalVictim = victim;
        }

        public void Dispose()
        {
            _vm.GlobalSelf = _oldSelf;
            _vm.GlobalOther = _oldOther;
            _vm.GlobalVictim = _oldVictim;
        }
    }
}
