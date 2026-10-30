using System.Collections.Generic;
using Gothic.Core.Extensions;
using Gothic.Core.Services;
using Gothic.Core.Services.Context;
using Reflex.Attributes;
using ZenKit;
using ZenKit.Daedalus;

namespace Gothic.Core.Domain.Npc
{
    /// <summary>
    /// The scripts' spell data, like the engine (OpenGothic GameScript::spellDesc): a spell id names its spell through
    /// spellFxInstanceNames[id] ("Firebolt"); its C_Spell instance is SPELL_FIREBOLT, its cast function
    /// Spell_Cast_Firebolt, its cast animations use spellFxAniLetters[id] ("FBT"). Read once per game VM. Owned by
    /// MagicService.
    /// </summary>
    public class SpellDataDomain
    {
        private const int _targetCollectNone = 0;
        private const int _targetCollectCaster = 1;
        private const int _targetCollectFocus = 2;
        private const int _targetCollectAllFallbackNone = 6;

        [Inject] private readonly GameStateService _gameStateService;
        [Inject] private readonly ContextGameVersionService _contextGameVersionService;

        private DaedalusVm _vm;
        private readonly Dictionary<int, SpellInstance> _spells = new();

        public string GetName(int spellId)
        {
            return GetArrayString("spellFxInstanceNames", spellId);
        }

        public string GetAniLetters(int spellId)
        {
            return GetArrayString("spellFxAniLetters", spellId);
        }

        /// <summary>
        /// The spell's C_Spell instance, null if the scripts have none.
        /// </summary>
        public SpellInstance GetSpell(int spellId)
        {
            ResetOnNewVm();
            if (_spells.TryGetValue(spellId, out var cached))
                return cached;

            SpellInstance spell = null;
            var name = GetName(spellId);
            var symbol = string.IsNullOrEmpty(name) ? null : _vm.GetSymbolByName($"SPELL_{name.ToUpperInvariant()}");
            if (_vm.IsInstanceOf(symbol, "C_SPELL"))
            {
                try
                {
                    spell = _vm.InitInstance<SpellInstance>(symbol);
                }
                catch (System.Exception)
                {
                    spell = null;
                }
            }

            _spells[spellId] = spell;
            return spell;
        }

        /// <summary>
        /// Like the engine (OpenGothic Item::isSpellShoot): a spell flies at its target unless it works on nobody, the
        /// caster, the focused NPC or everybody around - G1's Stormfist flies anyway.
        /// </summary>
        public bool IsProjectile(int spellId)
        {
            if (_contextGameVersionService.IsGothic1() && "Stormfist".EqualsIgnoreCase(GetName(spellId)))
                return true;

            var spell = GetSpell(spellId);
            if (spell == null)
                return false;

            return spell.TargetCollectAlgo is not (_targetCollectNone or _targetCollectCaster or _targetCollectFocus
                or _targetCollectAllFallbackNone);
        }

        private string GetArrayString(string arrayName, int index)
        {
            ResetOnNewVm();
            var array = _vm?.GetSymbolByName(arrayName);
            if (array == null || index < 0 || index >= array.Size)
                return null;

            return array.GetString((ushort)index);
        }

        private void ResetOnNewVm()
        {
            var vm = _gameStateService.GothicVm;
            if (_vm == vm)
                return;

            _vm = vm;
            _spells.Clear();
        }
    }
}
