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
    },
  },
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: ["./vitest.setup.ts"],
    include: ["src/**/*.{test,spec}.{ts,tsx}"],
  },
});
