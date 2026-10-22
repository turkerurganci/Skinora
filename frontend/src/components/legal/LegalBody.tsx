import type { ReactNode } from "react";

const BULLET = /^- /;
const NUMBERED = /^\d+\.\s/;

type Run = { kind: "p" | "ul" | "ol"; lines: string[] };

function kindOf(line: string): Run["kind"] {
  if (BULLET.test(line)) return "ul";
  if (NUMBERED.test(line)) return "ol";
  return "p";
}

/**
 * T168 — renders one legal/help text from the message catalogue.
 *
 * The catalogue holds plain strings (no markup), so the structure is carried
 * by line conventions: a blank line (`\n\n`) separates blocks; inside a block,
 * consecutive `- ` lines form a bulleted list, consecutive `1. ` lines a
 * numbered list, and any other consecutive lines one paragraph (so a lead-in
 * line such as "Purposes:" can sit directly above its list). Kept
 * deliberately tiny — the texts are authored by hand in four languages and
 * must stay readable as JSON.
 */
export function LegalBody({ text }: { text: string }) {
  const runs: Run[] = [];
  for (const block of text.split(/\n\s*\n/)) {
    let current: Run | null = null;
    for (const raw of block.split("\n")) {
      const line = raw.trim();
      if (line === "") continue;
      const kind = kindOf(line);
      if (!current || current.kind !== kind) {
        current = { kind, lines: [] };
        runs.push(current);
      }
      current.lines.push(line);
    }
  }

  return (
    <div className="mt-2 space-y-3 text-base leading-relaxed text-gray-700">
      {runs.map((run, i): ReactNode => {
        if (run.kind === "ul") {
          return (
            <ul key={i} className="list-disc space-y-1 pl-5">
              {run.lines.map((line, j) => (
                <li key={j}>{line.replace(BULLET, "")}</li>
              ))}
            </ul>
          );
        }
        if (run.kind === "ol") {
          return (
            <ol key={i} className="list-decimal space-y-1 pl-5">
              {run.lines.map((line, j) => (
                <li key={j}>{line.replace(NUMBERED, "")}</li>
              ))}
            </ol>
          );
        }
        return <p key={i}>{run.lines.join(" ")}</p>;
      })}
    </div>
  );
}
