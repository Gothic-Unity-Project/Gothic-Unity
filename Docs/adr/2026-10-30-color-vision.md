# ADR: A color vision option for color-blind players

- Status: proposed
- Date: 2026-10-30

## Context

Players with a red, green or blue weakness lose differences Gothic shows only by color: health and mana bars, ripe and unripe plants, magic effects, enemies against the grass. The accessibility list asked for colorblind modes. Gothic itself has none, and the VR menus are full.

## Decisions

1. **Correct, don't simulate.** The option shifts what a weak eye can't tell apart into colors it can (daltonization: the well-known simulation of the three weaknesses at full severity, and the usual shift of the lost difference into the remaining channels). It doesn't show how a color-blind person sees, which helps nobody playing.
2. **One color matrix in the post-processing.** A global volume with a channel mixer does it; Unity bakes it into the color grading table, so it costs one table lookup per pixel. The camera's post-processing is only switched on while a mode is on, so players who don't need it pay nothing, which matters on Quest 2.
3. **The option sits in the game menu under the text size**, like the text size itself: normal, red-weak, green-weak, blue-weak. It applies at once and is stored in Gothic.ini.

## Consequences

- Color-blind players can tell more of Gothic's colors apart; everybody else sees no change.
- With a mode on, Quest 2 renders an extra post-processing pass; its cost has to be measured on the headset.
- The correction is one strength for everybody; a strength slider can come if players ask for it.
