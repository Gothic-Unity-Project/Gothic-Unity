# ADR: Hits, knockout, death and experience like the engine

- Status: proposed
- Date: 2026-10-31
- Technical details: Docs/architecture.md (Daedalus VM, combat); ADR 2026-10-31-npc-perceptions.md

## Context

In Gothic a beaten human isn't dead: he lies unconscious, gets up later, and remembers who beat him; only enemies who hate you (or monsters) die. Death runs the scripts' death state, which gives experience and drops the inventory, and the people around learn about the murder or the defeat. Before, every hit with 0 hit points killed (also humans, so most quests with a duel broke), the damage was a placeholder, experience came for every death anywhere, and nobody noticed a body or a fight.

## Decisions

1. **Humans go down unconscious, unless the attacker hates them.** Like the engine: a human whose permanent attitude towards the attacker is hostile dies, every other human is knocked out (hit points 1, the scripts' unconscious state, the defeat is remembered and gives its experience). Monsters and orcs die. NPC archers always kill. Not chosen: knockout only by the hero - NPC duels and brawls ended deadly.
2. **Damage like the engine.** Strength plus weapon (or the NPC's own claws/fists) per damage type, minus the target's protection; immune types count nothing; at least the scripts' minimal damage. Gothic 1 adds critical hits by the weapon talent, Gothic 2 near misses by the hit chance.
3. **Dying runs the scripts' death with the killer.** Experience only for the hero's (or his party's) kills, the scripts mark only his victims as killed by him. Witnesses around get the murder or the defeat, the killer doesn't witness himself. A knockout pays experience once (the first victory over a human), finishing off an unconscious human pays nothing, a script killing an NPC pays nobody. The dead stay dead: no routine, no standing up when their state changes or they come back into view. The swing that knocks someone out doesn't finish him off.
4. **One place decides how an NPC goes down.** A script taking the last hit points (a curse, a summon fading away) goes through the same decision as a hit. Until now the hit points reaching 0 killed right away wherever they were set - that left no room for the knockout. Not chosen: keep the instant death and undo it for knockouts.
5. **A knocked out hero gets up after 10 seconds.** He can't move meanwhile; the NPCs who fought him stop, put their weapons away and forget their anger - the fight is over. Hits on the hero lying down don't count.
6. **No new settings** (wave 2 flag policy).

## Consequences

- Duels and brawls (Bullit, the arena, Mordrag) end with the loser unconscious, quests that need him alive keep working.
- The hero lying down doesn't see it yet in VR (body, camera) - that comes with the VR hero body PR, as does finishing off and the hero's own death.
- A script emptying the hero's hit points doesn't kill him yet (VR hero body PR); an NPC killed by a script while out of view lies dead but its death scripts run only when it is in view (save/load PR).
- Still open: NPCs dropping their weapon when they go down, picking it up again (NPC fight PR), ranged and magic hits (their PRs), getting XP for any kill as an option.
