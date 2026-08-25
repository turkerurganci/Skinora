---
name: reference_workflow_subagents_blocked
description: Workflow/Agent subagents — were org-policy-blocked 2026-06-14, but WORK again as of 2026-06-15; verify per session with one cheap probe
type: reference
---

**Status flips between sessions — always re-verify.**

- **2026-06-14 (WP1 F1 re-validation):** `Workflow` / `Agent` subagents FAILED — every spawned agent returned "Your organization has disabled Claude subscription access for Claude Code · Use an Anthropic API key instead, or ask your admin to enable access." The workflow still "completed" but with an **empty result** (0 findings) because no agent ran.
- **2026-06-15 (WP2 yapım):** subagents/workflows **WORK** — an Explore probe + a 6-agent parallel discovery workflow + completeness critic all returned rich, concrete findings. The earlier org-policy block did not reproduce. (Local VS Code, Opus 4.8.)

**How to apply:** Before relying on a multi-agent adversarial/discovery workflow, spawn **one cheap probe agent** and confirm it returns real content. If it does, use workflows freely. If agents come back with the org-policy error or empty/zero-finding results, treat that as "did not run" (NOT "nothing found / PASS") and fall back to doing the adversarial pass **inline yourself** (read code/tests/spec, run build+tests+CI directly). The block appears account/environment/session-dependent and transient — never assume either state without checking.
