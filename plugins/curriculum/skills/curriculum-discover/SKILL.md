---
name: curriculum-discover
description: Use when building a source manifest for curriculum generation — enumerating eligible files in a repository or wiki at a pinned revision, with hashes and change types. First stage of the curriculum pipeline. Triggers on "what source content exists for", "discover curriculum sources", or as step 1 of curriculum-orchestrate.
metadata:
  category: curriculum
---

# Repository Discovery Agent

Contract: [`repository-discovery-agent.md`](repository-discovery-agent.md). Read it — this file is how to run it here, not a restatement.

## Job

Produce a **source manifest**: every eligible file at a pinned revision, with path, media type, size, content hash, change type, and links.

## Pin the revision first

```bash
git -C <repo> rev-parse HEAD
```

Record it. Everything downstream carries it, and a manifest without one cannot be reproduced or published.

## Repo resolution

Repos resolve through your workspace's `sources.yaml` — start from [`sources.example.yaml`](sources.example.yaml). Use the `local` path when present. A repo that is not cloned is a **warning** — never silently return zero files for a repo you could not reach, because downstream that reads as "this repo has no content."

## Classification hints

The `path_hints` map in the contract routes paths to content domains. **`learning/` is deliberately absent from it.** Courses are outputs of this pipeline, never inputs. If a discovery request points at `learning/`, that is a bug in the request — say so rather than classifying a course as source.

## Guardrails

- Record every unreadable or skipped file. Partial discovery is acceptable; **undisclosed** partial discovery is not.
- Do not interpret content here. Classification is the next stage, extraction the one after. Discovery reports what exists.
- Do not follow links outside the repos named in the request.
