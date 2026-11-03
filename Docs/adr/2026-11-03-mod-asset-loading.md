# ADR: Loading big worlds and mod data like the engine

- Status: proposed
- Date: 2026-11-03

## Context

Destiny of Myrtana, a big Gothic 2 mod, showed where our loading differs from the original engine. Its first boot took about 85 seconds and used over 7 GB of memory, its own animations and mob scripts were ignored, objects the world marks as invisible were drawn (ore piles, dig spots), and its Ikarus scripts piled up values on the script stack until the scripts broke.

The original engine reads only the texture sizes it shows, prefers the compiled animation scripts over the text ones, hides what the world marks as invisible, and runs Ikarus against its own memory.

## Decisions

1. **Textures are read only in the sizes we keep.** The textures stay loaded as they come from the game files, and an image size is decoded only when it is shown. Before, every size of every texture was decoded twice up front. For Destiny of Myrtana: textures 1.8 s instead of 9.8 s, the whole world 13.4 s instead of 23.3 s, peak memory 5.2 GB instead of 7.6 GB. Not chosen: keep decoding everything up front - that cache once made Gothic 2's Dragon Island load fast, but the sizes it needed are still read from the loaded files without decoding.
2. **In Gothic 2, the compiled animation script comes before the text one.** Mods ship their changed animations and mob scripts only in the compiled file, while the text file can be the unchanged original. Gothic 1 keeps reading the text file as before. Not chosen: both games - nobody checked yet whether the Gothic 1 engine prefers the compiled file too.
3. **Objects the world marks as invisible are not drawn.** A plain invisible object also doesn't block the way, like in the engine. An invisible mob (a dig spot, a lever) stays usable. Not chosen: drawing everything as before - mods place invisible helpers the player shouldn't see.
4. **An Ikarus or LeGo function is only replaced when the mod declares it exactly as we expect.** Mods ship different versions of these libraries. If a mod declares a function with other parameters, its own script version runs instead of ours, with a warning. Ikarus functions that read the engine's list of script functions are answered from our own script data. Not chosen: replacing by name only - a wrong parameter list breaks the script stack for every later call.

## Consequences

- Worlds boot faster with much less memory. Gothic 2's Dragon Island load time hasn't been measured again since.
- Gothic 2's own data now uses the compiled animation scripts too. Gothic 1 mods that ship only changed compiled animations still show the old ones.
- In Gothic 2, the five treasure marks near the start, three levers and three floor plates are now invisible, because the world marks them so. Digging and using them still works. Whether VR should show the treasure marks anyway is open.
- Ikarus loops and jumps are still not run like in the engine; they are only taken off the stack correctly now.
