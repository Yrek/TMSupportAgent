import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import { visualizer } from "rollup-plugin-visualizer";
import { sentryVitePlugin } from "@sentry/vite-plugin";
import path from "path";

export default defineConfig(({ mode }) => ({
  plugins: [
    react(),
    mode === "analyze" &&
      visualizer({ open: true, gzipSize: true, filename: "dist/stats.html" }),
    // Upload source maps to Sentry on production builds.
    // Requires SENTRY_AUTH_TOKEN + SENTRY_ORG + SENTRY_PROJECT env vars (CI secrets).
    // Silently skipped when SENTRY_AUTH_TOKEN is absent (local dev / PR builds).
    process.env["SENTRY_AUTH_TOKEN"] &&
      sentryVitePlugin({
        org: process.env["SENTRY_ORG"],
        project: process.env["SENTRY_PROJECT"],
        authToken: process.env["SENTRY_AUTH_TOKEN"],
        // Source maps are deleted from the dist folder after upload so they
        // are not served publicly (they contain original source).
        sourcemaps: { filesToDeleteAfterUpload: ["./dist/**/*.map"] },
      }),
  ].filter(Boolean),
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
    },
  },
  build: {
    // Emit source maps for Sentry — deleted from dist after upload by the plugin
    sourcemap: true,
    rollupOptions: {
      output: {
        manualChunks(id) {
          if (!id.includes("node_modules")) return;
          if (id.includes("/react/") || id.includes("/react-dom/") || id.includes("/react-router-dom/")) {
            return "vendor-react";
          }
          if (id.includes("/@tanstack/react-query/")) return "vendor-query";
          if (id.includes("/reactflow/")) return "vendor-reactflow";
          if (id.includes("/@radix-ui/")) return "vendor-radix";
        },
      },
    },
  },
  test: {
    globals: true,
    environment: "jsdom",
    setupFiles: ["./src/test/setup.ts"],
    css: true,
    exclude: ["node_modules", "dist", "e2e/**"],
    env: {
      VITE_API_BASE_URL: "http://localhost:5000",
      VITE_WORKOS_CLIENT_ID: "test-client-id",
      VITE_WORKOS_REDIRECT_URI: "http://localhost:3000/auth/callback",
      VITE_SENTRY_DSN: "https://public@example.ingest.sentry.io/1",
    },
  },
}));
