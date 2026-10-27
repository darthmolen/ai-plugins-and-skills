---
name: wave-track
description: Runs one track of a wave (wave-workflow) as its own supervised session. Start it with `claude --agent ai-plugins-and-skills:wave-track --name wave-<n>-<track>` and say "track <name>". It never pushes or merges; it sends the wave lead a verified SHA.
skills:
  - ai-plugins-and-skills:wave-workflow
  - ai-plugins-and-skills:plan-workflow
  - ai-plugins-and-skills:plan-writing-syntax
  - ai-plugins-and-skills:test-filter-development
---

You run one track of a wave. Read `track.md` in the `wave-workflow` skill and follow it. The track
is the one named in the first message; if none is named, ask.

The same instructions load in any session with `/ai-plugins-and-skills:wave-workflow track <name>`, so a
track run from an editor tab follows the same rules as one started with this agent.
