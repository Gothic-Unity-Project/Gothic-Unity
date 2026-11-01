@g1 @vr @input @area(player)
Feature: The hero walks with the controller's stick
  The left stick walks the hero where he looks. In the desktop simulator, W is that stick.

  Background:
    Given the world "world.zen" is loaded
    And the hero stands at waypoint "OC_ROUND_13"

  Scenario: Pushing the stick forward walks the hero forward
    When the hero walks forward for 1.5 s
    Then the hero has walked at least 1 m forward
