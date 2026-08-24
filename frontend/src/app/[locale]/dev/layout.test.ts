import { afterEach, describe, expect, it, vi } from "vitest";

const notFound = vi.hoisted(() =>
  vi.fn(() => {
    throw new Error("NEXT_NOT_FOUND");
  }),
);

vi.mock("next/navigation", () => ({ notFound }));

/**
 * WP2b (`dev-route-visibility`) — the component gallery must 404 on a
 * production deployment and stay reachable everywhere else.
 *
 * `NODE_ENV` is read at module scope by the bundler-inlined `process.env`, so
 * each case re-imports the layout with the value already set rather than
 * flipping it mid-test.
 */
async function renderUnderNodeEnv(value: string) {
  vi.stubEnv("NODE_ENV", value as "production" | "development" | "test");
  vi.resetModules();
  const { default: DevLayout } = await import("./layout");
  return () => DevLayout({ children: null });
}

describe("dev route gate", () => {
  afterEach(() => {
    vi.unstubAllEnvs();
    notFound.mockClear();
  });

  it("404s in production", async () => {
    const render = await renderUnderNodeEnv("production");
    expect(render).toThrow("NEXT_NOT_FOUND");
    expect(notFound).toHaveBeenCalledOnce();
  });

  it.each(["development", "test"])("stays reachable in %s", async (env) => {
    const render = await renderUnderNodeEnv(env);
    expect(render).not.toThrow();
    expect(notFound).not.toHaveBeenCalled();
  });
});
