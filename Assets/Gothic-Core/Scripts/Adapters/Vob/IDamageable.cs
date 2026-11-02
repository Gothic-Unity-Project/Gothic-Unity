using Gothic.Core.Models.Container;

namespace Gothic.Core.Adapters.Vob
{
    /// <summary>
    /// A VOB that reacts to being hit, like ZenGin's OnDamage on zCTrigger/oCTriggerScript/zCMover with
    /// "reactToOnDamage" (e.g. the switches in the Sleeper's temple you shoot with a bow).
    /// </summary>
    public interface IDamageable
    {
        /// <param name="attacker">Who shot/cast (null if unknown).</param>
        void OnDamage(NpcContainer attacker);
    }
}
