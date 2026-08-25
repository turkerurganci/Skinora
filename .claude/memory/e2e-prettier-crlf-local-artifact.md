---
name: e2e-prettier-crlf-local-artifact
description: Local `prettier --check` in e2e/ flags files on this Windows box due to core.autocrlf — it's a checkout artifact, not a defect; CI (LF) is authoritative
type: project
---

On this Windows machine `git config core.autocrlf=true` and `.gitattributes` only has `* text=auto` (no explicit `eol=lf` for `.ts`), so all `e2e/*.ts` are checked out with CRLF on disk. The e2e `.prettierrc.json` sets no `endOfLine`, so prettier defaults to `"lf"` → `npm run format:check` in `e2e/` flags most/all files **locally**. This is a checkout artifact, NOT a formatting defect.

**Why:** During T111 validation, `prettier --check` flagged 11/12 e2e `.ts` files (incl. T111's `src/api.ts`, `src/db.ts`). Converting them to LF in-place made prettier pass, confirming CRLF was the only issue.

**How to apply:** Don't treat local e2e prettier warnings as a finding. The authoritative check is CI's blocking **"1. Lint" job** which runs `npm run format:check` in `e2e/` on a Linux/LF checkout — if that job is `success`, the committed files are prettier-clean. To verify a specific file locally, convert to LF first (`sed -i 's/\r$//' <file>`) then re-run, and restore with `git checkout --`. Same pattern applies to any JS workspace check (sidecar-fake, frontend) on this box. Related: [[wp5-validation-pass]].
