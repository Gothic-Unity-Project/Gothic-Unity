@g1 @vr @area(lighting) @adr(2026-11-01-torch-hero-light)
Feature: A torch in the hand lights the world like a flame
  The burning torch in the player's hand is the one sharp light. Off by default; while it is on, the torch's own
  soft light is off; letting go fades it out; putting it out switches it off at once.

  Background:
    Given the world "world.zen" is loaded
    And the hero stands at waypoint "OCR_HUT_1"
    And it is 23:00

  Scenario: With the option off, the lit torch keeps its own light
    Given the Gothic.ini option "torchHeroLight" is off
    And the hero holds a "ITLSTORCH" in his right hand
    When the hero lights the torch in his right hand
    Then the torch in his right hand is burning
    And the hero light is off
    And the torch's own light is on

  Scenario: With the option on, the lit torch in the hand is the hero light
    Given the Gothic.ini option "torchHeroLight" is on
    And the hero holds a "ITLSTORCH" in his right hand
    When the hero lights the torch in his right hand
    Then the hero light is on
    And the torch's own light is off

  # Measured +29 % here at 23:00 (the camp's fires light the ground already); 20 % leaves room for the flicker.
  @visual
  Scenario: The sharp light brightens the ground near the flame
    Given the Gothic.ini option "torchHeroLight" is on
    And the hero holds a "ITLSTORCH" in his right hand
    And the ground 1 m in front of the hero is remembered as "unlit"
    When the hero lights the torch in his right hand
    Then the ground 1 m in front of the hero is at least 20% brighter than "unlit"

  Scenario: Two burning torches, the one grabbed last lights the world
    Given the Gothic.ini option "torchHeroLight" is on
    And the hero holds a "ITLSTORCH" in his left hand
    And the hero lights the torch in his left hand
    When the hero holds a "ITLSTORCH" in his right hand
    And the hero lights the torch in his right hand
    Then the hero light is on
    And the torch's own light is off
    And the torch in his right hand is the hero torch

  Scenario: Letting go fades the sharp light out and brings the normal light back
    Given the Gothic.ini option "torchHeroLight" is on
    And the hero holds a "ITLSTORCH" in his right hand
    And the hero lights the torch in his right hand
    When the hero lets go of the item in his right hand
    Then the torch's own light is on
    And the hero light is on
    And the hero light is off within 2 s

  Scenario: Putting the torch out switches the sharp light off at once
    Given the Gothic.ini option "torchHeroLight" is on
    And the hero holds a "ITLSTORCH" in his right hand
    And the hero lights the torch in his right hand
    When the hero puts out the torch in his right hand
    Then the torch in his right hand is out
    And the hero light is off

  Scenario: Switching the option off while a lit torch is held
    Given the Gothic.ini option "torchHeroLight" is on
    And the hero holds a "ITLSTORCH" in his right hand
    And the hero lights the torch in his right hand
    When the Gothic.ini option "torchHeroLight" is off
    Then the hero light is off within 1 s
    And the torch's own light is on

  @input
  Scenario: The simulator's hand grips a torch and its trigger lights it
    Given a "ITLSTORCH" lies 0.5 m in front of the hero
    When the hero crouches
    And the hero grips the "ITLSTORCH" lying in front of him with his right hand
    And the hero pulls the trigger of his right hand
    Then the torch in his right hand is burning

  @manual
  Scenario: The light flickers and the flame glows in the headset
    Given the Gothic.ini option "torchHeroLight" is on
    And the hero holds a "ITLSTORCH" in his right hand
    When the hero lights the torch in his right hand and walks along the Old Camp's palisade at night
    Then the pools of light move with the torch without steps, the light flickers gently and the flame has a soft glow
