using System.Collections.Generic;
using Gothic.Core.Domain.Movement;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Movement;
using Gothic.Core.Models.Vm;
using Reflex.Attributes;
using UnityEngine;
using ZenKit.Daedalus;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Movement
{
    /// <summary>
    /// Gothic's ledge climbing rules: which ledges the hero and NPCs can climb and how (see LedgeProbeDomain and
    /// Docs/ledge-climbing.md). The values come from the scripts' GIL_VALUES.
    /// </summary>
    public class LedgeService
    {
        [Inject] private readonly GameStateService _gameStateService;

        private readonly LedgeProbeDomain _probeDomain = new LedgeProbeDomain().Inject();
        private readonly Dictionary<int, LedgeRules> _guildRules = new();

        private GuildValuesInstance _rulesValues;
        private LedgeRules _rules;
        private GuildValuesInstance _guildRulesValues;

        /// <summary>
        /// Climbing values of the hero. The hero is human, its values are in the GIL_HUMAN slot (see
        /// ZenKitExtension.GetValuesGuild). Without them (scripts not loaded yet), Gothic's values are used.
        /// Built once per GIL_VALUES: the scripts don't change them, a new VM brings a new instance.
        /// </summary>
        public LedgeRules HeroRules
        {
            get
            {
                var values = _gameStateService.GuildValues;

                // Before the scripts are loaded (boot): nothing to warn about yet.
                if (values == null)
                    return LedgeRules.GothicHumanDefaults;

                if (!ReferenceEquals(values, _rulesValues))
                {
                    _rules = CreateHeroRules(values);
                    _rulesValues = values;
                }

                return _rules;
            }
        }

        /// <summary>
        /// Climbing values of a guild's GIL_VALUES slot (pass ZenKitExtension.GetValuesGuild for an NPC). Values the
        /// guild leaves out are Gothic's human ones. Built once per guild and GIL_VALUES.
        /// </summary>
        public LedgeRules GetRules(int guild)
        {
            var values = _gameStateService.GuildValues;
            if (values == null)
                return LedgeRules.GothicHumanDefaults;

            if (!ReferenceEquals(values, _guildRulesValues))
            {
                _guildRules.Clear();
                _guildRulesValues = values;
            }

            if (!_guildRules.TryGetValue(guild, out var rules))
            {
                rules = LedgeRules.FromGuildValues(values, guild);
                _guildRules[guild] = rules;
            }

            return rules;
        }

        /// <summary>
        /// A ledge an NPC root at position (restHeight above its feet) can climb along direction - see
        /// LedgeProbeDomain.ProbeNpcClimb.
        /// </summary>
        public LedgeInfo ProbeNpcClimb(Vector3 position, float restHeight, Vector3 direction, LedgeRules rules)
        {
            return _probeDomain.ProbeNpcClimb(position, restHeight, direction, rules);
        }

        /// <summary>
        /// What Gothic's jump does for a body standing at feet and looking along forward.
        /// </summary>
        public LedgeInfo ProbeAhead(Vector3 feet, Vector3 forward, LedgeBody body)
        {
            return _probeDomain.ProbeAhead(feet, forward, body, HeroRules);
        }

        /// <summary>
        /// A ledge at a point near its lip (a hand), counted from the floor the climb starts from.
        /// </summary>
        public LedgeInfo ProbeEdge(Vector3 nearPoint, Vector3 approach, float baseFeetY, LedgeBody body)
        {
            return _probeDomain.ProbeEdge(nearPoint, approach, baseFeetY, body, HeroRules);
        }

        /// <summary>
        /// A target on top of a ledge (a teleport target), approached from from. The ledge counts from the floor at
        /// the wall's foot.
        /// </summary>
        public LedgeInfo ProbeTop(Vector3 target, Vector3 from, LedgeBody body)
        {
            return _probeDomain.ProbeTop(target, from, body, HeroRules);
        }

        private static LedgeRules CreateHeroRules(GuildValuesInstance values)
        {
            const int guild = (int)VmGothicEnums.Guild.GIL_HUMAN;
            if (values.GetJumpUpHeight(guild) <= 0 || values.GetJumpLowHeight(guild) <= 0)
            {
                Logger.LogWarning("[Ledge] No climbing values of GIL_HUMAN in GIL_VALUES. Using Gothic's.",
                    LogCat.Misc);
                return LedgeRules.GothicHumanDefaults;
            }

            return LedgeRules.FromGuildValues(values, guild);
        }
    }
}
