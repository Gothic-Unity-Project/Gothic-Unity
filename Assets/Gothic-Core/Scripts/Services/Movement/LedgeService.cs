using Gothic.Core.Domain.Movement;
using Gothic.Core.Extensions;
using Gothic.Core.Logging;
using Gothic.Core.Models.Movement;
using Gothic.Core.Models.Vm;
using Reflex.Attributes;
using UnityEngine;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Services.Movement
{
    /// <summary>
    /// Gothic's ledge climbing rules for the hero: which ledges can be climbed and how (see LedgeProbeDomain and
    /// Docs/ledge-climbing.md). The values come from the scripts' GIL_VALUES.
    /// </summary>
    public class LedgeService
    {
        [Inject] private readonly GameStateService _gameStateService;

        private readonly LedgeProbeDomain _probeDomain = new LedgeProbeDomain().Inject();

        private bool _isMissingValuesLogged;

        /// <summary>
        /// Climbing values of the hero. The hero is human, its values are in the GIL_HUMAN slot (see
        /// ZenKitExtension.GetValuesGuild). Without them (scripts not loaded yet), Gothic's values are used.
        /// </summary>
        public LedgeRules HeroRules
        {
            get
            {
                var values = _gameStateService.GuildValues;
                const int guild = (int)VmGothicEnums.Guild.GIL_HUMAN;

                // Before the scripts are loaded (boot): nothing to warn about yet.
                if (values == null)
                    return LedgeRules.GothicHumanDefaults;

                if (values.GetJumpUpHeight(guild) <= 0 || values.GetJumpLowHeight(guild) <= 0)
                {
                    if (!_isMissingValuesLogged)
                    {
                        _isMissingValuesLogged = true;
                        Logger.LogWarning("[Ledge] No climbing values of GIL_HUMAN in GIL_VALUES. Using Gothic's.",
                            LogCat.Misc);
                    }

                    return LedgeRules.GothicHumanDefaults;
                }

                var defaults = LedgeRules.GothicHumanDefaults;
                return new LedgeRules
                {
                    StepHeight = Metres(values.GetStepHeight(guild), defaults.StepHeight),
                    JumpLowHeight = Metres(values.GetJumpLowHeight(guild), defaults.JumpLowHeight),
                    JumpMidHeight = Metres(values.GetJumpMidHeight(guild), defaults.JumpMidHeight),
                    JumpUpHeight = Metres(values.GetJumpUpHeight(guild), defaults.JumpUpHeight),
                    SlideAngle = Degrees(values.GetSlideAngle(guild), defaults.SlideAngle),
                    ClimbHeadingAngle = Degrees(values.GetClimbHeadingAngle(guild), defaults.ClimbHeadingAngle)
                };
            }
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

        /// <summary>
        /// A value of 0 counts as missing, like the whole GIL_VALUES (a script without it).
        /// </summary>
        private static float Metres(int centimetres, float fallback)
        {
            return centimetres > 0 ? centimetres.ToMeter() : fallback;
        }

        private static float Degrees(int degrees, float fallback)
        {
            return degrees > 0 ? degrees : fallback;
        }
    }
}
