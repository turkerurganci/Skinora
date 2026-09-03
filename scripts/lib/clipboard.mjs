import { spawnSync } from "child_process";

/**
 * Copy text to the OS clipboard. Returns the tool used, or null when no
 * clipboard tool is available (the caller then relies on the written file).
 *
 * Windows note that is easy to get wrong: `clip.exe` decodes its stdin as the
 * console's ANSI codepage, which mangles Turkish characters. Feeding it
 * UTF-16LE with a BOM is the encoding it reads losslessly — the questions this
 * carries are Turkish, so this is not optional polish.
 */
export function copyToClipboard(text) {
  const attempts =
    process.platform === "win32"
      ? [{ cmd: "clip", args: [], encode: (t) => utf16leWithBom(t) }]
      : process.platform === "darwin"
        ? [{ cmd: "pbcopy", args: [], encode: (t) => Buffer.from(t, "utf8") }]
        : [
            { cmd: "wl-copy", args: [], encode: (t) => Buffer.from(t, "utf8") },
            { cmd: "xclip", args: ["-selection", "clipboard"], encode: (t) => Buffer.from(t, "utf8") },
          ];

  for (const attempt of attempts) {
    try {
      const result = spawnSync(attempt.cmd, attempt.args, {
        input: attempt.encode(text),
        windowsHide: true,
      });
      if (!result.error && result.status === 0) return attempt.cmd;
    } catch {
      // try the next tool
    }
  }
  return null;
}

function utf16leWithBom(text) {
  return Buffer.concat([Buffer.from([0xff, 0xfe]), Buffer.from(text, "utf16le")]);
}
