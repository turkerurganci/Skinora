import { spawnSync } from "child_process";
import { readFileSync, existsSync, mkdirSync, rmSync, readdirSync } from "fs";
import { resolve, join } from "path";
import { tmpdir, homedir } from "os";

/**
 * Codex CLI transport — the API-key-free path. Codex signs in with the
 * owner's ChatGPT subscription, so a question costs nothing beyond the plan
 * that is already paid for. This is OpenAI's own sanctioned automation
 * surface; driving chatgpt.com in a browser is not (and is against the terms).
 *
 * MEASURED on 2026-09-03, codex-cli 0.153.0 (Windows 10):
 *   - `codex exec -` reads the prompt from stdin.
 *   - `-o <file>` writes the agent's last message; stdout carries progress.
 *   - `-s read-only` is the sandbox; there is NO `-a/--ask-for-approval` flag
 *     in this version (the plan assumed one from the docs — the probe
 *     corrected it).
 *   - `codex login status` prints "Logged in using ChatGPT" and exits 0 EVEN
 *     WHEN THE TOKEN IS DEAD. Its 401 only surfaces on a real call. So the
 *     status check is used as a cheap pre-filter, never as proof: an auth
 *     failure is detected from the call's own output and reported as such.
 */

/**
 * Auth diagnostics, anchored to codex's own error lines.
 *
 * The unanchored version of this was a real defect, found by measurement:
 * `codex exec` echoes the PROMPT and the ANSWER into stderr, so scanning the
 * combined output meant a question about "JWT refresh token rotation" — or any
 * answer mentioning "401 Unauthorized" — was diagnosed as a dead session. The
 * successful, already-paid-for answer was then deleted by the finally block
 * and the owner was told to log in again. Same input, different topic word,
 * opposite outcome.
 *
 * Two defences now: this pattern only matches codex's own `Error:`/`ERROR`
 * diagnostic lines, and it is consulted ONLY when the answer file came back
 * empty — a call that produced an answer succeeded, whatever words appear in
 * its transcript.
 */
export const AUTH_FAILURE =
  /^\s*(?:Error|ERROR|error)\b[^\n]*(?:refresh token|Please log out and sign in again|401 Unauthorized|Not logged in|Unauthorized)/m;

/** Default model. Always sent explicitly — ~/.codex/config.toml may pin a stale one. */
export const DEFAULT_CODEX_MODEL = "gpt-5.6-sol";

/**
 * Resolve an executable this process can actually spawn.
 *
 * On Windows, `npm i -g` installs SHIMS — codex.cmd / codex.ps1 / a bash
 * script — and none of them is spawnable without a shell (Node refuses to
 * execute .cmd without `shell:true` since CVE-2024-27980). "codex is on my
 * PATH" and "Node can run codex" are therefore different claims, and the first
 * one silently reported "not installed" until the native binary was located.
 *
 * Search order: CODEX_BIN override → bare name (real exe on PATH, the
 * Linux/macOS case) → the platform package's vendored binary under the npm
 * global prefix.
 */
let cachedBin;
export function resolveCodexBin() {
  if (cachedBin !== undefined) return cachedBin;

  const candidates = [];
  if (process.env.CODEX_BIN) candidates.push(process.env.CODEX_BIN);
  candidates.push("codex");

  if (process.platform === "win32") {
    const roots = [
      process.env.APPDATA && join(process.env.APPDATA, "npm"),
      join(homedir(), "AppData", "Roaming", "npm"),
    ].filter(Boolean);
    for (const root of roots) {
      const vendorRoot = join(root, "node_modules", "@openai", "codex", "node_modules", "@openai");
      if (!existsSync(vendorRoot)) continue;
      for (const pkg of safeReaddir(vendorRoot)) {
        const triples = join(vendorRoot, pkg, "vendor");
        for (const triple of safeReaddir(triples)) {
          const exe = join(triples, triple, "bin", "codex.exe");
          if (existsSync(exe)) candidates.push(exe);
        }
      }
    }
  }

  for (const candidate of candidates) {
    const r = spawnSync(candidate, ["--version"], { encoding: "utf8", windowsHide: true });
    if (!r.error && r.status === 0) {
      cachedBin = { bin: candidate, version: (r.stdout || "").trim() };
      return cachedBin;
    }
  }
  cachedBin = null;
  return cachedBin;
}

function safeReaddir(dir) {
  try {
    return readdirSync(dir);
  } catch {
    return [];
  }
}

/** Version string when codex is spawnable, else null. */
export function codexAvailable() {
  const resolved = resolveCodexBin();
  return resolved ? resolved.version : null;
}

/** Cheap pre-filter only — see the auth note above. */
export function codexLoginStatus() {
  const resolved = resolveCodexBin();
  if (!resolved) return { ok: false, text: "codex bulunamadi" };
  const r = spawnSync(resolved.bin, ["login", "status"], { encoding: "utf8", windowsHide: true });
  const out = `${r.stdout || ""}${r.stderr || ""}`.trim();
  return { ok: !r.error && r.status === 0, text: out };
}

/**
 * Ask Codex one question and return its final message.
 *
 * The agent runs in an EMPTY temp directory with `--skip-git-repo-check`, not
 * in the repo. Two reasons, both deliberate: the owner approves an exact
 * question text and only that text should reach the model — an agent free to
 * read the working tree would answer partly from material nobody approved —
 * and it removes the file-touching surface entirely.
 *
 * @returns {{text: string, model: string, durationMs: number}}
 * @throws  Error with .authFailure = true when the session needs re-login.
 */
export function askCodex({ prompt, model, effort = "high", timeoutMs = 300000 }) {
  const resolved = resolveCodexBin();
  if (!resolved) throw new Error("codex calistirilabilir bulunamadi");
  const codexBin = resolved.bin;
  const chosenModel = model || DEFAULT_CODEX_MODEL;

  // Unique per call, not per process: a PID-derived name is reused after a
  // PID rolls over, and a leftover file from an earlier run would be read back
  // as this question's answer. Removed up front as well, belt and braces.
  const unique = `${process.pid.toString(36)}-${counter++}-${Date.now().toString(36)}`;
  const workDir = resolve(tmpdir(), `gorus-${unique}`);
  const outFile = resolve(tmpdir(), `gorus-${unique}.out.md`);
  try { rmSync(outFile, { force: true }); } catch { /* best effort */ }
  mkdirSync(workDir, { recursive: true });

  const args = [
    "exec", "-",
    "-s", "read-only",
    "--skip-git-repo-check",
    "--ephemeral",
    "-C", workDir,
    "-m", chosenModel,
    "-c", `model_reasoning_effort="${effort}"`,
    "-o", outFile,
  ];

  const started = Date.now();
  try {
    const r = spawnSync(codexBin, args, {
      input: prompt,
      encoding: "utf8",
      timeout: timeoutMs,
      windowsHide: true,
      maxBuffer: 64 * 1024 * 1024,
    });
    const durationMs = Date.now() - started;

    // Read the ANSWER FIRST. A call that produced one succeeded, whatever
    // words appear in the transcript — the transcript contains the question
    // and the answer, so judging auth from it misreads the topic as an error.
    const text = existsSync(outFile) ? readFileSync(outFile, "utf8").trim() : "";
    if (text) return { text, model: chosenModel, durationMs };

    // Empty answer — now, and only now, work out why.
    const diagnostics = r.stderr || "";
    if (AUTH_FAILURE.test(diagnostics)) {
      const err = new Error(
        "Codex oturumu gecersiz — `codex login --device-auth` ile yeniden gir.",
      );
      err.authFailure = true;
      throw err;
    }
    if (r.error) throw new Error(`codex calistirilamadi: ${r.error.message}`);
    throw new Error(
      `codex bos cevap dondu (exit=${r.status}). Son tani satirlari: ${lastErrorLines(diagnostics)}`,
    );
  } finally {
    try { rmSync(outFile, { force: true }); } catch { /* best effort */ }
    try { rmSync(workDir, { recursive: true, force: true }); } catch { /* best effort */ }
  }
}

let counter = 0;

/**
 * Only codex's own diagnostic lines, never the echoed prompt/answer — an error
 * message that quotes the question back would leak approved-but-private text
 * into logs and, worse, read as if the tool had said it.
 */
function lastErrorLines(stderr, limit = 3) {
  const lines = stderr
    .split(/\r?\n/)
    .filter((l) => /^\s*(?:Error|ERROR|error|warning|WARN)\b/.test(l))
    .slice(-limit);
  return lines.length ? lines.join(" | ") : "(tani satiri yok)";
}
