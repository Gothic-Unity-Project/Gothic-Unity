# AI state machine and perceptions

## State loop (`AiHandler`)

1. `Start` → calls `ZS_X()` once
2. `Loop` → calls `ZS_X_Loop()` every tick until it returns non-zero (LOOP_END)
3. `End` → calls `ZS_X_End()` once
4. `AfterEnd` → next routine

`GlobalSelf/GlobalOther` are set from the NPC's stored state-other at the start of each tick, not from the original
call site.

## `AI_StartState` is queued, not immediate

`AI_StartState` enqueues a `StartState` action - the entry function runs on a later tick. If C# needs an aivar set
before any perception can see it (e.g. `AIV_WASDEFEATEDBYSC` on knockout), set it from C# **before** queueing.

Clearing the queue on `AI_StartState(..., stopCurrentState=true)` can wipe actions queued just before it in the
same script (e.g. `AI_StandUp` then `AI_StartState(ZS_HealSelf)`). Defer the clear when the queue holds StandUp /
UndrawWeapon.

`AI_StartState(..., 0, ...)` (no end function) skips `ZS_X_End` - any cleanup there (like
`B_ResetTempAttitude`) never runs. Check whether C# needs to compensate.

## Who fires which perception

- Fired from C#, handled in Daedalus: `PERC_ASSESSPLAYER`, `PERC_ASSESSENEMY`, `PERC_ASSESSDAMAGE`,
  `PERC_ASSESSOTHERSDAMAGE`, `PERC_DRAWWEAPON`, `PERC_ASSESSREMOVEWEAPON`, `PERC_ASSESSFIGHTER`.
- Verify the current list in `NpcAiService` / `FightService` / `AiHandler` before assuming one is missing - check
  `PERC_ASSESSBODY`, `PERC_ASSESSFIGHTSOUND` in particular.
- Humans vs monsters: monsters (`ZS_MM_*`) register `PERC_ASSESSOTHERSDAMAGE`; human routine states usually do not.
  Don't fall back to monster logic for humans.

## Attitudes

- `Npc_GetAttitude` = effective (temp) attitude. `Npc_GetPermAttitude` must return **only** the permanent one.
- `Npc_SetPermAttitude` is a Daedalus helper that sets both perm and temp.
- The guild attitude table (`GIL_ATTITUDES` in `GUILDS.D`) covers human guilds only; monster-vs-monster needs
  explicit handling (e.g. summoner ↔ summon friendly).

## Target selection traps

Several C# paths write an NPC's target/enemy (next-target, perception update, active-attacker, armed-threat,
FightService damage reactions). Every one of them must apply the same exclusions:
- attitude Friendly → skip
- same guild → skip
- party member / summon (`AIV_MM_PARTYMEMBER`) → skip
- prefer keeping the current valid target (stickiness) instead of re-picking from scratch every tick

Sensing: hearing/smell ignore field of view but **not** walls - use a line-of-sight check with FOV disabled.

## Useful script files

`AI_Constants.d`, `C_Functions.d` (`C_NpcIsDown`, `C_NpcIsHuman`, `C_AmIWeaker`, `C_BodyStateContains`),
`ZS_Attack.d`, `ZS_AssessEnemy.d`, `ZS_ReactToDamage.d`, `ZS_ProclaimAndPunish.d`, `B_AssessFighter.d`,
`B_SelectWeapon.d`, `Externals.d`, `GUILDS.D`.
