# ADR: One log file per session

- Status: proposed
- Date: 2026-11-11
- Technical details: CLAUDE.md (Logging)

## Context

A build wrote its log into one file, overwritten at every start. A play test or a stream that crashed or was restarted lost the log of the run before, and the lines only had the seconds since the start, so they couldn't be matched to a video.

## Decisions

1. **Every run of a build writes its own log file, named after its start time, in a "Logs" folder next to the old file; the newest 30 are kept.** A bug report can name the run, and older runs aren't lost by a restart. Not chosen: one growing file (hard to send, never shrinks).
2. **Every line starts with the local time, then the seconds since the start.** A stream's video can be matched to the log line by line. The seconds are counted from the start in universal time, so a clock change (daylight saving time) doesn't make them jump.
3. **No setting.** It's how logging should work in a build. The Editor still writes no log file, it has its console.

## Consequences

- The log is now at `Logs/Gothic-Unity_<date>_<time>.log.txt` in the game's data folder instead of `Gothic-Unity.log.txt`. Anyone asking players for a log has to ask for the newest file in that folder.
- 30 files at most stay on the disk (on the Quest too).
