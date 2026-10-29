# ADR: World sounds are decoded in the background

- Status: proposed
- Date: 2026-10-28

## Context

Every ambient sound in the world (wind, birds, the river, the In Extremo concert in the Old Camp) was turned from the game's files into playable audio at the moment its object was built. Reading the file and handing it to Unity takes a few milliseconds, but decoding it takes much longer for long sounds: the concert alone took about 0.4 seconds, the bird ambience in the woods almost 0.1 seconds. During that time the game stood still, which showed as hitches after loading and while walking around.

## Decisions

1. **Decoding happens on a background thread.** Reading the file and creating the audio stay on the main thread, as only they need Unity, and they are fast. The sound object is built at once and gets its audio a moment later.
2. **A sound that should be playing starts when its audio arrives.** Looping sounds start then, random ones start their timer then. Not chosen: waiting with the whole object until the audio is ready, which would delay everything else in the queue.
3. **A sound used by many objects is decoded once.** Objects that ask for the same sound while it's being decoded wait for the same result.
4. **Only world sounds use this for now.** NPC voices and other sounds still load directly, as they are short and need to play at a given moment.

## Consequences

- Measured in the editor: decoding all 58 sound files of the Gothic 1 world took 0.6 seconds, reading them 0.08 and creating the audio 0.05. The concert now blocks the game for about 13 milliseconds instead of 0.4 seconds, the bird ambience in the woods for about 19 instead of 86.
- An ambient sound may start a fraction of a second after its object appears. Far sounds are inaudible anyway, and with the black screen after loading the near ones are ready before the view opens.
- The decoder may not use Unity or shared state anymore, as it runs on another thread.
