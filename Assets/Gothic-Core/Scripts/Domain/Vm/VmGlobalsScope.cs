using System;
using ZenKit;

namespace Gothic.Core.Domain.Vm
{
    /// <summary>
    /// Sets self/other/victim (and item, if given) for one script call and restores the previous values on Dispose:
    /// using (new VmGlobalsScope(vm, self, other, victim)) vm.Call(function);
    /// Like the engine (OpenGothic invokeState), self/other/victim are set as they are, null included - a left-over
    /// victim made B_AssessWarn or B_AssessUseMob see a crime that wasn't there. Item is only set when given (e.g.
    /// PERC_ASSESSTHEFT's taken item), other calls keep the current one.
    /// A struct, so a perception call per NPC and tick allocates nothing.
    /// </summary>
    public readonly struct VmGlobalsScope : IDisposable
    {
        private readonly DaedalusVm _vm;
        private readonly DaedalusInstance _oldSelf;
        private readonly DaedalusInstance _oldOther;
        private readonly DaedalusInstance _oldVictim;
        private readonly DaedalusInstance _oldItem;
        private readonly bool _isItemSet;

        public VmGlobalsScope(DaedalusVm vm, DaedalusInstance self, DaedalusInstance other, DaedalusInstance victim,
            DaedalusInstance item = null)
        {
            _vm = vm;
            _oldSelf = vm.GlobalSelf;
            _oldOther = vm.GlobalOther;
            _oldVictim = vm.GlobalVictim;
            _isItemSet = item != null;
            _oldItem = _isItemSet ? vm.GlobalItem : null;
            vm.GlobalSelf = self;
            vm.GlobalOther = other;
            vm.GlobalVictim = victim;
            if (_isItemSet)
                vm.GlobalItem = item;
        }

        public void Dispose()
        {
            _vm.GlobalSelf = _oldSelf;
            _vm.GlobalOther = _oldOther;
            _vm.GlobalVictim = _oldVictim;
            if (_isItemSet)
                _vm.GlobalItem = _oldItem;
        }
    }
}
