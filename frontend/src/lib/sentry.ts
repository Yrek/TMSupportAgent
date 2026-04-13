/**
 * Sentry initialisation and capture helpers.
 *
 * Call initSentry() once at app startup (main.tsx) when VITE_SENTRY_DSN is set.
 * Use captureToSentry() from logger.ts — never call Sentry directly from components.
 *
 * Source maps are uploaded automatically by @sentry/vite-plugin at build time
 * using SENTRY_AUTH_TOKEN (CI secret, never bundled into the client).
 *
 * sendDefaultPii: false — no personal data sent (CLAUDE.md §10.4).
 */
import * as Sentry from "@sentry/react";

let initialised = false;

export function initSentry(dsn: string, release?: string | undefined): void {
  if (initialised) return;
  Sentry.init({
    dsn,
    release,
    environment: import.meta.env.MODE,
    sendDefaultPii: false,
    // Capture all errors, 10 % of performance traces
    tracesSampleRate: 0.1,
  });
  initialised = true;
}

export function captureToSentry(
  message: string,
  stack: string | undefined,
  extra: Record<string, string>,
): void {
  if (!initialised) return;
  const error = new Error(message);
  if (stack) error.stack = stack;
  Sentry.captureException(error, { extra });
}
