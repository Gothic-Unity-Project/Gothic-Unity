# ADR: The trade counter holds the dialog

- Date: 2026-11-17
- Replaces: part of decision 4 of [2026-11-03-vr-movement-trade-theft](2026-11-03-vr-movement-trade-theft.md) (the counter doesn't hold the dialog, its choices come back at once)

## Context

Since the trade counter, the dialog's choices came back the moment trading started, so the player could talk while trading. In the headset the choice box sits behind the counter: reaching for goods or pointing at the counter picked a choice by accident and started its lines (#530).

In the original game the trade screen holds the dialog: the choices are neither shown nor usable while it is open, and they come back when it closes. The reference engine does the same.

## Decisions

1. **While the counter is open, the dialog shows no choices.** They come back when the trade ends: closed with X or sealed with OK. Not chosen: keeping the choices and moving the box out of reach, or ignoring them while a hand is over the counter. Both keep accidental picks possible and need tuning in the headset.
2. **Ending the dialog still ends the trade.** Walking away, or anything else that ends the dialog, closes the counter and gives the offers back, as before. The choices don't come back then.
3. **A trader who can't trade any more ends the conversation too.** Knocked out, dead or with a drawn weapon he closes the counter, the offers go back and the dialog is over. Not chosen: bringing the choices back, because a trader who is down never does and the hero would stay held.

## Consequences

- Trading works like in the original: trade first, then go on talking.
- Talking while trading is gone. To end the conversation during a trade, close the counter first.
- A mode without a counter (Flat) ends the trade at once and gets its choices back, as before.
