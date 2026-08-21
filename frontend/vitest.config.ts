import { fileURLToPath } from "node:url";
import { defineConfig } from "vitest/config";
import react from "@vitejs/plugin-react";

// Vitest runner for the Next.js frontend (WP18). jsdom env for component
// tests; the "@/" alias mirrors tsconfig paths (vitest does not read them
// automatically). Next's own bundler/build is unaffected — these deps are
// dev-only and never imported by the app.
export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      "@": fileURLToPath(new URL("./src", import.meta.url)),
      // T135 — next-intl's `createNavigation` imports the extensionless
      // `next/navigation`, which Next 16 exposes through its bundler plugin
      // rather than through package `exports`. Vite cannot resolve it, so any
      // test that reaches `@/i18n/navigation` (everything importing the
      // `@/components/common` barrel, since LanguageSelector lives there)
      // failed to load before the first assertion ran. Pointing at the real
      // file next to it is the whole fix; Next's own build is unaffected.
      "next/navigation": fileURLToPath(
        new URL("./node_modules/next/navigation.js", import.meta.url),
      ),
    },
  },
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: ["./vitest.setup.ts"],
    include: ["src/**/*.{test,spec}.{ts,tsx}"],
    server: {
      // Process next-intl through Vite instead of leaving it externalised, so
      // the `next/navigation` alias above actually applies to ITS imports —
      // which is where the extensionless specifier lives.
      deps: { inline: ["next-intl"] },
    },
  },
});
