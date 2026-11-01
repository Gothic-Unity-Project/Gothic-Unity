@g1 @vr @area(dialog) @adr(2026-10-30-dialog-pins-hero)
Feature: An NPC can open a dialog up to 8 m away
  Pinned dialogs (default): a dialog opens up to 8 m, not farther; the hero is held where he stood until it ends.
  Free movement (option): the old 5 m rule.

  Background:
    Given the world "world.zen" is loaded
    And the hero stands at waypoint "OC_ROUND_13"
    And it is 12:00

  # Known bug: an NPC is built 1 m beside the spot it was put at (npc-spawn.feature), so the distance can be off.
  @known-bug
  Scenario Outline: The start distance decides
    Given "GRD_283_Gardist" stands <distance> m in front of the hero
    And "GRD_283_Gardist" is <distance> m from the hero, give or take 0.3 m
    When "GRD_283_Gardist" starts a dialog with the hero
    Then a dialog is <result>

    Examples:
      | distance | result   |
      | 7.5      | open     |
      | 8.5      | not open |

  Scenario: Too far away, the NPC logs why and goes on
    Given "GRD_283_Gardist" stands 9 m in front of the hero
    When "GRD_283_Gardist" starts a dialog with the hero
    Then a dialog is not open
    And the log contains "hero farther than 8 m - dialog not started" within 1 s

  Scenario: With free movement on, the old 5 m rule applies
    Given the developer switch "EnableDialogFreeMovement" is on
    And "GRD_283_Gardist" stands 6 m in front of the hero
    When "GRD_283_Gardist" starts a dialog with the hero
    Then a dialog is not open

  @input
  Scenario: The hero is pinned while the dialog is open and free after it
    Given "GRD_283_Gardist" stands 3 m in front of the hero
    When "GRD_283_Gardist" starts a dialog with the hero
    Then a dialog is open
    And the hero is not able to walk
    When the hero ends the dialog
    Then the hero is able to walk

  @manual
  Scenario: Turning stays possible during a dialog
    Given a dialog is open with an NPC behind the hero
    When the hero turns around with the stick
    Then he faces the NPC and stays where he stood
