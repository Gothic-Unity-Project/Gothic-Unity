# 4. NPC movement: navmesh, water, falling, ladders

[← Overview](README.md)

## What changed

- **Runtime NavMesh** for chases: NPCs avoid obstacles, use ladders, jump down to the hero from small heights,
  stop at ledges instead of falling out of the world. Waypoint network still drives routines.
- **Water:** monsters that can swim follow along the surface and don't fight while in water; others stop at the
  shore; flyers cross. NPCs wade and swim at the surface.
- **Falling and climbing:** fall animation without mid-air steering, fall damage above 5 m (can kill), ledge
  climbing up to ~2 m, sliding along walls instead of walking into them.
- **Ladders:** smooth climbing — stance in front of the rungs, no root hiccup between cycles, snap to the top.
- Skeletons no longer float or fall through geometry; NPC-NPC separation.

## Key commits

`768c676d` water + NavMesh · `150d31c2` falling, climbing, walls · `b4380dfe` ladders · `28191858` skeletons ·
`c272029e` separation · `0ac83519` GoToNpc via waypoints

## Main files

`Services/Npc/NpcNavMeshService.cs` (new, +423) · `Services/Npc/NpcWaterService.cs` (new) ·
`Adapters/Npc/NpcJumpFall.cs` (new, +647) · `Domain/Npc/Actions/AnimationActions/GoToNpc.cs`

## Config flags

`EnableNpcNavMesh`, `EnableNpcWater`, `EnableNpcJumpAndFall`, `EnableNpcWallCollision`.

## Review notes

- ⚠️ **NavMesh is baked at runtime with one agent type (human).** Big monsters can snag on passages. Bake cost
  on Quest is unmeasured.
- ⚠️ `NpcJumpFall.cs` (647 lines) lives in `Gothic-Core/Adapters` and contains most of the fall/climb logic.
  The physics stepping belongs in the adapter, the decisions (can climb? fall damage?) could be a domain class.
- ℹ️ A static one-shot log flag (`_hasLoggedLadderCycle`) — harmless, but static state in an adapter.

## How to test

- Run away over a small cliff; the NPC jumps after you. Lure a wolf into deep water.
- G1: an NPC climbing a ladder (e.g. the Old Camp towers).
