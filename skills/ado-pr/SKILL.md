---
name: ado-pr
description: Deprecated name; invoke pr-create.
metadata:
  category: tools
---

# ado-pr (deprecated)

This skill was renamed `pr-create`. Invoke `ai-plugins-and-skills:pr-create` and follow it; its
`references/ado.md` holds everything this skill said about Azure DevOps.

The old name is kept so existing references keep working, and it will be removed in a future major
version of this plugin. `pr-create` opens pull requests on Azure DevOps and GitHub alike, so the
callers no longer pick a skill per host.
