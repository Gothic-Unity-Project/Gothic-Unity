using GUZ.Core.Domain.Vm;

namespace GUZ.Core.Services.Vm
{
    public class VmExternalService
    {
        private VmExternalDomain _domain = new();
        private VmIkarusLeGoDomain _ikarusLeGoDomain = new();

        
        public void RegisterExternals()
        {
            _domain.RegisterExternals();
        }

        public void RegisterIkarusLeGo()
        {
            _ikarusLeGoDomain.Init();
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
