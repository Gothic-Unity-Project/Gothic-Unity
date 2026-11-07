# ADR: The VR hero's own body

- Status: proposed
- Date: 2026-10-31
- Technical details: VRHeroBody, VRHeroBodyAnimator (Gothic-VR/Scripts/Adapters/Player); DeveloperConfig "VR Hero Body"

## Context

In VR the player saw floating hands and nothing below them: no armor, no legs, nothing when knocked out. Gothic shows the hero's body, his armor and his animations, and NPCs react to that body.

## Decisions

1. **The hero's own model under the VR head.** It's his current armor, rebuilt when it changes. Legs and torso play his Gothic animations (idle, walk, run, backwards, turning, swimming). The arms reach for the VR hands with a two-bone IK. The model's head and hands are hidden. Not chosen: a separate VR avatar - it would not show the armor or mod models.
2. **Our own sampler, not the NPC AnimationSystem.** The body samples the baked Gothic tracks itself (VRHeroBodyAnimator). The NPC system drives root motion and the AI queue; the VR body only follows the player.
3. **The hero's fists are the VR hands by default.** The Immersion option "Hero's hands" is on by default, because the fists match the visible hero body. Players can switch it off in the Immersion menu to get the VR hand models back. The team decided on Gothic fists without fingers; haptics are enough.
4. **Knocked out or dead, the camera falls with the body** (height only, never sideways or turned). Where the player looks stays theirs, so it doesn't make them sick. Turning with the stick stays possible while knocked out (seated players).
5. **No colliders and nothing saved.** The body is rebuilt from the hero's visual.

## Consequences

- Each frame the body costs one IK solve, the sampling of a few tracks and two short ray checks.
- The ray checks find the water surface and the bottom, so the body can swim or float above the bottom.
- Each armor change rebuilds the body and cuts the arm and sleeve parts out of its meshes again. This is not per frame.
- Footsteps from the body's animations, swim sounds and hiding the body during a transformation come with the movement and magic PRs.
