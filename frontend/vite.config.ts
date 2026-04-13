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
        manualChunks: {
          "vendor-react": ["react", "react-dom", "react-router-dom"],
          "vendor-query": ["@tanstack/react-query"],
          "vendor-reactflow": ["reactflow"],
          "vendor-radix": [
            "@radix-ui/react-dialog",
            "@radix-ui/react-dropdown-menu",
            "@radix-ui/react-label",
            "@radix-ui/react-select",
            "@radix-ui/react-separator",
            "@radix-ui/react-tabs",
            "@radix-ui/react-toast",
            "@radix-ui/react-tooltip",
          ],
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
    },
  },
}));
