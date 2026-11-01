@g1 @vr @area(vobs) @adr(2026-10-31-lock-picking-rules)
Feature: Lock picking by Gothic 1's rules
  Everybody picks locks; the talent's value is the chance that a wrong turn breaks the pick (90 untrained, 50, 10).

  Background:
    Given the world "world.zen" is loaded
    And the hero stands at waypoint "WP_INTRO_SHORE"
    And the dice are seeded with 7

  Scenario Outline: The talent's value is the chance to break
    Given the hero's talent "Picklock" has value <value>
    Then the lock pick breaks on <percent>% of 2000 wrong turns, give or take 3

    Examples:
      | value | percent |
      | 90    | 90      |
      | 50    | 50      |
      | 10    | 10      |
      | 0     | 0       |

  Scenario: Everybody can pick locks
    Given the hero's talent "Picklock" has skill 0
    Then the hero can pick locks

  @manual
  Scenario: A wrong turn plays the game's message and sound
    Given the hero holds a lock pick in a chest's lock
    When he turns it the wrong way
    Then the pick-lock sound plays and the message of the scripts appears
