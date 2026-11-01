@g1 @vr @area(npc) @adr(2026-10-30-theft-and-mob-use)
Feature: NPCs notice theft
  The NPCs around are told when the hero takes an item lying in the world; their scripts decide what it means.
  Only items lying in the world count; the hero's own things taken out of the backpack don't.

  # The guard is one the world doesn't place yet (chapter 4's free mine guard). His routine leads him to the way point
  # the hero stands at, so he comes towards the hero and sees him. The item is a lock pick: food held at the mouth is
  # eaten in VR (the simulator's hand is close to the head), and an arrow taken goes out of sight at once.
  Background:
    Given the world "world.zen" is loaded
    And the hero stands at waypoint "FMC_PATH27"
    And it is 12:00
    And "GRD_283_Gardist" stands 3 m in front of the hero

  # Known bug: AI_Quicklook isn't an external of the game yet (BoroBongo's perception branches add it). G1's
  # B_AssessTheft calls it first and makes no further call after it, so no NPC reacts to a theft.
  @known-bug
  Scenario: The owner catches the thief
    Given a "ITKELOCKPICK" lies 1 m in front of the hero
    And the "ITKELOCKPICK" in front of the hero belongs to "GRD_283_Gardist"
    When the hero takes the "ITKELOCKPICK" lying in front of him
    Then "GRD_283_Gardist" is in state "ZS_CATCHTHIEF" within 8 s

  # Known bug: as above - the guard runs B_AssessTheft up to AI_Quicklook and no further.
  @known-bug
  Scenario: A guard of items comes for anyone taking an item
    Given "GRD_283_Gardist" guards the items around him
    And a "ITKELOCKPICK" lies 1 m in front of the hero
    When the hero takes the "ITKELOCKPICK" lying in front of him
    Then "GRD_283_Gardist" is in state "ZS_CATCHTHIEF" within 8 s

  Scenario: An item nobody owns or guards may be taken
    Given a "ITKELOCKPICK" lies 1 m in front of the hero
    When the hero takes the "ITKELOCKPICK" lying in front of him
    Then "GRD_283_Gardist" is not in state "ZS_CATCHTHIEF" for 8 s

  Scenario: Taking a thing out of the backpack is no theft
    Given "GRD_283_Gardist" guards the items around him
    When the hero takes a "ITKELOCKPICK" out of his backpack
    Then "GRD_283_Gardist" is not in state "ZS_CATCHTHIEF" for 8 s

  Scenario: Taking the same item a second time is noticed once
    Given a "ITKELOCKPICK" lies 1 m in front of the hero
    When the hero takes the "ITKELOCKPICK" lying in front of him
    And the hero lets go of the item in his right hand
    And the hero takes the "ITKELOCKPICK" lying in front of him
    Then the log contains "[HeroPerception] The hero takes" exactly 1 time within 8 s

  @manual
  Scenario: A real grab in the headset is noticed
    Given the hero stands in front of a guard of the Old Camp with an item lying between them
    When the hero grabs the item with his hand
    Then the guard shouts "Hands off" and comes for the hero
