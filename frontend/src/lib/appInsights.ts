/**
 * Azure Application Insights client.
 *
 * Provides:
 *  - Exception tracking (via trackException)
 *  - Automatic page view tracking (enableAutoRouteTracking)
 *  - Automatic API dependency tracking — URL, duration, status code only.
 *    Request/response bodies are never captured (CLAUDE.md §10.4).
 *  - Distributed tracing: injects W3C traceparent headers on fetch calls so
 *    backend spans are correlated with frontend spans in Azure Monitor.
 *
 * Call initAppInsights() once at startup when VITE_APPINSIGHTS_CONNECTION_STRING is set.
 * No-op otherwise — app works without it in local dev.
 */
import { ApplicationInsights, type IExceptionTelemetry } from "@microsoft/applicationinsights-web";

let client: ApplicationInsights | null = null;

export function initAppInsights(connectionString: string, appVersion?: string | undefined): void {
  if (client) return;

  client = new ApplicationInsights({
    config: {
      connectionString,
      ...(appVersion ? { appId: appVersion } : {}),

      // Page view tracking on each React Router navigation
      enableAutoRouteTracking: true,
      autoTrackPageVisitTime: true,

      // API dependency tracking (URL + duration + status — no bodies)
      disableAjaxTracking: false,
      disableFetchTracking: false,

      // Distributed tracing: correlate browser → API spans in Azure Monitor
      distributedTracingMode: 2, // W3C Trace Context (traceparent header)
      enableCorsCorrelation: true,
      correlationHeaderDomains: ["*"], // override in production with your API domain

      // Privacy: never capture PII (CLAUDE.md §10.4)
      disableCookiesUsage: false, // session cookie only, no user identity
      excludeRequestFromAutoTrackingPatterns: [
        // Exclude WorkOS auth endpoints — they may carry tokens in query strings
        /workos\.com/,
      ],
    },
  });

  client.loadAppInsights();
  client.trackPageView(); // Capture the initial load
}

export function trackExceptionInAppInsights(
  message: string,
  stack: string | undefined,
  properties: Record<string, string>,
): void {
  if (!client) return;

  const error = new Error(message);
  if (stack) error.stack = stack;

  const telemetry: IExceptionTelemetry = {
    exception: error,
    properties,
  };

  client.trackException(telemetry);
}
