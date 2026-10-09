# Physics layers

Verify against `ProjectSettings/TagManager.asset` before relying on numbers - they can change.

| # | Name | Usage |
|---|---|---|
| 0 | Default | generic / world geometry |
| 1 | TransparentFX | VRMouth trigger sphere |
| 2 | Ignore Raycast | mesh sub-objects |
| 4 | Water | water zones |
| 5 | UI | UI |
| 6 | VobItem | world items (default for item prefab) |
| 7 | VobItemNoWorldCollision | item while grabbed (reverts to VobItem after ~1 s / on release) |
| 8 | Player | player body |
| 9 | DynamicPose | dynamic NPC poses |
| 10 | VobNpcOrMonster | NPC/monster bodies |
| 11 | VobMovable | chests, doors |
| 12 | VobHitbox | NPC hit-detection colliders |
| 20 | Grabbable | HVR grabbables |
| 21 | Hand | HVR hands |

## Collision matrix

Stored in `ProjectSettings/DynamicsManager.asset`, field `m_LayerCollisionMatrix`: 256 hex chars = 32 rows × uint32
little-endian. Row i bit j = layer i collides with layer j. Row N starts at hex offset N×8; first byte = layers 0-7.

The matrix is symmetric - when adding a pair, set the bit in **both** rows. Moving a prefab to a new layer silently
breaks every trigger that only listened to the old layer (e.g. items moved Default → VobItem stopped VRMouth from
detecting food until TransparentFX ↔ VobItem/VobItemNoWorldCollision bits were added).

Important pairs: TransparentFX ↔ VobItem / VobItemNoWorldCollision (eating), VobItem ↔ VobHitbox (weapon hits),
VobItem ↔ Default (items rest on the world).
