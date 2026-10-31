# ADR: NPC perceptions and AI states like the engine

- Status: proposed
- Date: 2026-10-31
- Technical details: Docs/architecture.md (Daedalus VM, AI); ADR 2026-10-31-branch-split-wave-2.md

## Context

The scripts decide what an NPC does, but the engine decides what it notices: who hears a warning, who sees a body, when a state ends. Several of these engine parts were missing or simplified. A guard's warning reached only the guard himself, nobody reacted to a body, hearing and smell noticed the hero all around in the full range (orcs from 20 m behind them, standing still), a guide friendly to the hero was friendly to every monster too, and a dialog that ended in a fight skipped the dialog's end (the hero stayed invincible, nobody talked to or fought him anymore).

## Decisions

1. **A perception an NPC sends goes to every NPC around it, like the engine.** Range from the scripts, capped by each receiver's senses, the sender, the hero, the dead and the knocked out don't get it. A perception that is a state (a murder, a defeat) starts as the receiver's new state, others are just called. Not chosen: only the sender, as before - warnings and murders went unnoticed. A perception sent from inside another one is dropped after two levels (with a warning), so two NPCs can't answer each other forever. Not chosen: queuing it for the next frame like OpenGothic - no script we run sends from inside a perception, and dropping keeps the order of the scripts' calls.
2. **Senses close to the engine.** Sight in front within the full range, hearing only noise (walking, running) within half of it, smell only within 3 m, and none of them through walls. The half range and the 3 m are tuned in play, not taken from the engine. Guides and followers still smell all around, so they wait for a lagging hero and notice monsters on the way. Not chosen: noticing all around within the full range - sneaking past anyone was impossible in VR.
3. **An NPC's own attitude counts only towards the hero.** Between two NPCs only the guild table counts, like the engine. Not chosen: the own attitude for everyone - a guide friendly to the hero never fought monsters.
4. **States end like in the engine.** Starting a new state with "end the current one" runs the old state's end when the new one is reached, after the dialog lines still queued; a state without a loop ends at once and the NPC goes back to its routine. The startled jump around (whirl around) and turning away are real animations now.
5. **An NPC looking away ignores the hero and forgets its anger when done.** A deliberate difference to the scripts: they never reset the temporary hostility, the NPC looked away, saw the hero and started over forever.
6. **No new settings.** The branch's switches for these fixes are dropped, the fixed behaviour is the only one (wave 2 flag policy).

## Consequences

- NPCs warn each other, report bodies, notice the hero less easily from behind, and guides fight monsters.
- Each NPC that reacts to bodies looks for the closest one on every perception tick - fine for the camps tested, to watch on Quest.
- Still open: the hero's own perceptions (using a chest or door, footsteps, entering a hut - next PR), drawing a weapon in VR (VR hero body PR), taking items (VR inventory PR), noticing items lying around.
