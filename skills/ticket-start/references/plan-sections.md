# Plan sections

Every plan this skill writes carries these two sections, wherever local guidance says plans live.
Format and frontmatter follow `plan-workflow` and `plan-writing-syntax` where the repository has a
`planning/` board.

## Local guidance

List, in this order:

- The files read: root and nested instruction files, the user-level ones, and the formatting,
  analyzer and build configuration.
- What applies at each checkpoint: branch naming and base branch, where plans live, commit and
  staging policy, build, format and test commands, end-to-end expectations, documentation
  expectations, PR conventions, and what the agent may touch.
- Where local guidance overrides this skill or a chained skill, with the rule and its source.
- What was assumed because guidance was silent, and any contradiction between two sources and how it
  was resolved.

Name only the guidance that exists in the repository the plan is for.

## Verification plan

- The projects to build.
- The format and analyzer check on the changed files.
- The test suites, adjacent ones included, and how they must run.
- The end-to-end decision, with its reason: either the card or harness to use, or why none applies.
- The feature-flag states to cover.
- The documentation to update.
- The isolated per-session environment the tests and end-to-end runs use, if local guidance
  provides one, and any shared resources the work still needs outside it, with the lock name each
  takes.

Where the ticket has no acceptance criteria, say so here and state what the plan took as the
criteria, so the caller can correct it.
