using GUZ.Core.Domain.Vm;

namespace GUZ.Core.Services.Vm
{
    public class VmExternalService
    {
        private VmExternalDomain _domain = new();
        
        public void RegisterExternals()
        {
            _domain.RegisterExternals();
        }

        /// <summary>
        /// Gothic2 isn't calling this external function in NotR within Daedalus. Therefore, we set it here.
        /// </summary>
        public void ExchangeGuildAttitudes(string name)
        {
            _domain.Wld_ExchangeGuildAttitudes(name);
        }
    }
}
