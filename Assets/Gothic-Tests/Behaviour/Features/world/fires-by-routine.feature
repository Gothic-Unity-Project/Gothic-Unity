@g1 @vr @area(world) @adr(2026-10-31-fires-by-routine)
Feature: Fires burn when the scripts say so
  Gothic 1 lights its fireplaces from 22:00 to 05:00; by day they are out. Fires without a schedule keep burning.
  A fire's parts sit on the fire's slot.

  Background:
    Given the world "world.zen" is loaded
    And the hero stands at waypoint "OCR_CAMPFIRE_A_MOVEMENT1"

  Scenario: Camp fires burn at night
    Given it is 23:00
    Then every "FIREPLACE" fire within 30 m is burning

  Scenario: Camp fires are out by day
    Given it is 12:00
    Then every "FIREPLACE" fire within 30 m is out

  Scenario: The schedule switches at its minute
    Given it is 21:59
    And every "FIREPLACE" fire within 30 m is out
    When 1 game minute passes
    Then every "FIREPLACE" fire within 30 m is burning

  Scenario: Fires the scripts don't schedule keep burning by day
    Given it is 12:00
    Then every "FIRE" fire within 30 m is burning

  Scenario: Every flame sits on its bowl
    Given it is 23:00
    Then the flame of every fire within 30 m sits within 20 cm of its slot

  @slow
  Scenario: After loading a save at night the fires burn again once the scripts set the schedule
    Given it is 23:00
    When the game is saved and loaded again
    Then every "FIREPLACE" fire within 30 m is burning within 10 s
