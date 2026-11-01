@g2 @vr @area(vobs) @adr(2026-10-31-lock-picking-rules)
Feature: Lock picking by Gothic 2's rules
  Only a hero with the talent picks locks; the pick breaks when dexterity in a hundred doesn't hold.

  Background:
    Given the world "newworld.zen" is loaded
    And the dice are seeded with 7

  Scenario: Only a hero with the talent picks locks
    Given the hero's talent "Picklock" has skill 0
    Then the hero cannot pick locks
    When the hero's talent "Picklock" has skill 1
    Then the hero can pick locks

  Scenario: Dexterity in a hundred holds the pick
    Given the hero's dexterity is 30
    Then the lock pick breaks on 70% of 2000 wrong turns, give or take 3

  @manual
  Scenario: A breaking pick may be heard
    Given the hero picks a lock next to a guard in Khorinis
    When the pick breaks
    Then the guard may react to the sound, as the scripts decide
