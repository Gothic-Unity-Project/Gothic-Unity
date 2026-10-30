# ADR: Fighting NPCs run around obstacles along the waynet

- Status: proposed
- Date: 2026-10-31

## Context

An NPC chasing the hero ran straight at him, so a hut, a fence or a rock between them stopped it for good. Gothic's NPCs find their way around: when they lose sight of their enemy during a fight they follow the world's network of waypoints towards it. BoroBongo's branch solves this with a navigation mesh built at runtime around the player.

## Decisions

1. **Like Gothic, the waynet shows the way.** An NPC that can't see its enemy, though it's near enough to sense it, runs from the waypoint nearest to it towards the one nearest to the enemy, and straight at the enemy again once it sees it. The route is kept while the enemy stays put and found again when it moves away.
2. **No navigation mesh built at runtime.** Its building time and memory on Quest 2 are unknown, it would be rebuilt as the player walks and misses objects that load later. The waynet is already loaded and costs one sight check per running step.

## Consequences

- NPCs reach the hero around huts and walls wherever the waynet leads there, as in Gothic.
- Where the waynet doesn't lead, NPCs still get stuck; sliding along walls and climbing small ledges can follow.
