@g1 @vr @area(world)
Feature: The game boots into a world
  The scenarios share one running game: the first one boots it into its world, the ones after it play there.

  Scenario: The world is loaded
    Given the world "world.zen" is loaded
