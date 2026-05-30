using GUZ.Core.Const;
using GUZ.Core.Services;
using Reflex.Attributes;
using ZenKit.Vobs;

namespace GUZ.Core.Adapters.Properties.Vobs
{
    public class MovableProperties : VobProperties2
    {
        [Inject] private readonly GameStateService _gameStateService;
        
        public MovableProperties(IVirtualObject vob) : base(vob)
        { }
        
        public override string GetFocusName()
        {
            var nameSymbol = _gameStateService.GothicVm.GetSymbolByName($"MOBNAME_{VobAs<IMovableObject>()?.FocusName}");

            if (nameSymbol == null)
                return string.Empty;
            else
                return nameSymbol.GetString(0);
        }
    }
}
