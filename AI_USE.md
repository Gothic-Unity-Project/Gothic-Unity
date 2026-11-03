# How we use AI

We don't do vibe coding. We do **agentic engineering** (reference: [Andrej Karpathy on X](https://x.com/karpathy/status/2019137879310836075)).
> **Agentic Engineering:** The team designs the architecture, sets the direction and reviews the results.
> An AI agent builds parts of the code for us under that guidance.

## Why we do it

When we started, we assumed that rebuilding Gothic in Unity would be a decade-long side quest.
A community project with limited time and a lot of ambition. 
Then the IT landscape shifted.
Thanks to the development of AI technology, a real share of the work can now be done by an agent under our guidance.
So we decided to use those benefits for our project, instead of pretending nothing has changed.

Writing Gothic Unity (VR) entirely by hand today would be like writing it in Notepad instead of an IDE,  or in assembler instead of C#. 
Possible, and admittedly a bit romantic these days.

## Our ground rules

- Architecture, design decisions and game direction come from the team.
- Every AI-assisted change is reviewed and tested by a human before it lands, and a human on the team owns it.
- We don't use AI to generate game assets, art or audio.
- Gothic's original assets are never part of this repository. They are loaded at runtime from your own Gothic installation.
- We are open about it: assume most of this codebase (starting Q3 2026) is AI-assisted under the team's guidance. We say so here once instead of tagging every commit, and a human stays the author of each change.

## How we instruct the agent

Our agent setup is public: see [CLAUDE.md](CLAUDE.md) and [.claude/](.claude/). It holds the conventions the agent follows
in this repository, such as architecture and coding style.

## For contributors

AI-assisted contributions are welcome, with honest disclosure. Details are in [CONTRIBUTING.md](CONTRIBUTING.md)
and in the pull request template.

In short:

- Tell us how much AI helped and what your human effort was.
- A human did test and review the pull request.
- Uninvited autonomous bots opening pull requests are not welcome.

## Changes to this document

This reflects how we work today (October 2026). Tools and our practices might change, as well as this page.
Questions or suggestions are welcome on Discord or in a GitHub issue.
