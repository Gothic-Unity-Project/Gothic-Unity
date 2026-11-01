using Gothic.Core.Extensions;
using ZenKit.Vobs;

namespace Gothic.Core.Adapters.Properties.Vobs
{
    public class VobProperties2
    {
        protected readonly IVirtualObject Vob;

        public VobProperties2(IVirtualObject vob)
        {
            this.Inject();
            Vob = vob;
        }
        
        public virtual void Init()
        {
            
        }

        /// <summary>
        /// The mob scheme Daedalus asks for, e.g. Wld_IsMobAvailable (self,"BED"). See ZenKitExtension.GetMobScheme.
        /// </summary>
        public string GetVisualScheme()
        {
            return Vob.GetMobScheme();
        }
        
        public virtual string GetFocusName()
        {
            return Vob?.Name;
        }
        
        /// <summary>
        /// Shorthand function.
        /// </summary>
        protected T VobAs<T>() where T : IVirtualObject
        {
            return (T)Vob;
        }
    }
}
