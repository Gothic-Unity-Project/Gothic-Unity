using Gothic.Core.Logging;
using Gothic.Core.Models.Container;
using Logger = Gothic.Core.Logging.Logger;

namespace Gothic.Core.Domain.Npc
{
    /// <summary>
    /// Transformation scrolls (Spell_Logic_Trf_*): like the engine (OpenGothic Npc::TransformBack), scripts see the
    /// transformed hero as the monster - its guild is the monster's (wolves are friendly, hunters attack). Swaps
    /// Instance.Guild, Vob.Guild and Vob.GuildTrue and restores them. The body (puppet, camera) is the platform's.
    /// Not saved: FIXME a save while transformed stores the monster's guild.
    /// Owned by NpcService.
    /// </summary>
    public class HeroTransformDomain
    {
        private int _originalGuild;
        private int _originalVobGuild;
        private int _originalVobGuildTrue;

        public bool IsTransformed { get; private set; }


        public void Transform(NpcContainer hero, NpcContainer monster)
        {
            if (hero == null || monster?.Instance == null)
            {
                Logger.LogWarning("[HeroTransform] No hero or monster - the guild stays.", LogCat.Npc);
                return;
            }

            // Transformed again: the originals are still the hero's own guild.
            if (!IsTransformed)
            {
                _originalGuild = hero.Instance.Guild;
                _originalVobGuild = hero.Vob.Guild;
                _originalVobGuildTrue = hero.Vob.GuildTrue;
            }

            var guild = monster.Instance.Guild;
            hero.Instance.Guild = guild;
            hero.Vob.Guild = guild;
            hero.Vob.GuildTrue = guild;
            IsTransformed = true;
            Logger.Log($"[HeroTransform] Hero's guild {_originalGuild} -> {guild}.", LogCat.Npc);
        }

        /// <param name="hero">Null (no hero, e.g. while a world loads): only the transformed state ends.</param>
        public void TransformBack(NpcContainer hero)
        {
            if (!IsTransformed)
                return;

            IsTransformed = false;
            if (hero == null)
            {
                Logger.LogWarning("[HeroTransform] No hero to give the guild back to.", LogCat.Npc);
                return;
            }

            hero.Instance.Guild = _originalGuild;
            hero.Vob.Guild = _originalVobGuild;
            hero.Vob.GuildTrue = _originalVobGuildTrue;
            Logger.Log($"[HeroTransform] Hero's guild back to {_originalGuild}.", LogCat.Npc);
        }
    }
}
