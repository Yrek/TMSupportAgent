/**
 * Structured client-side error logger.
 *
 * Rules (CLAUDE.md §10.4):
 *  - Never log tokens, credentials, or personal data.
 *  - Log error message, stack, and correlation ID only.
 *
 * In production, errors are forwarded to Sentry when VITE_SENTRY_DSN is set.
 * Stack traces are readable because @sentry/vite-plugin uploads source maps at build time.
 */
import { captureToSentry } from "./sentry";
import { trackExceptionInAppInsights } from "./appInsights";

export interface ErrorLogEntry {
  correlationId: string;
  timestamp: string;
  message: string;
  stack?: string | undefined;
  componentStack?: string | undefined;
  context?: string | undefined;
}

export function generateCorrelationId(): string {
  return crypto.randomUUID();
}

export function logError(entry: Omit<ErrorLogEntry, "timestamp">): void {
  const logEntry: ErrorLogEntry = {
    ...entry,
    timestamp: new Date().toISOString(),
  };

  // Structured JSON log — survives transport to any log aggregator
  console.error("[TMA]", JSON.stringify(logEntry));

  // Forward to Sentry (no-op if not initialised)
  captureToSentry(entry.message, entry.stack, {
    correlationId: entry.correlationId,
    context: entry.context ?? "",
    componentStack: entry.componentStack ?? "",
  });

  // Forward to Application Insights (no-op if not initialised)
  trackExceptionInAppInsights(entry.message, entry.stack, {
    correlationId: entry.correlationId,
    context: entry.context ?? "",
    componentStack: entry.componentStack ?? "",
  });
}
