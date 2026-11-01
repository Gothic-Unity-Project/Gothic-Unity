@g1 @vr @area(npc)
Feature: NPCs appear where they are put
  An NPC put into the world - by the scripts, a summoning, the cheat menu or a scenario - is built at its spot. The
  game only moves it aside when something else stands there.

  Background:
    Given the world "world.zen" is loaded
    And the hero stands at waypoint "OC_ROUND_13"
    And it is 12:00

  # Known bug: when the NPC is built, the check for a free spot finds the NPC's own new body and moves it 1 m aside.
  @known-bug
  Scenario: A spawned NPC is built at its spot
    Given "GRD_283_Gardist" stands 5 m in front of the hero
    Then "GRD_283_Gardist" is built where he was put, give or take 20 cm
