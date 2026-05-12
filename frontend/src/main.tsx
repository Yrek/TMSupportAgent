import { createRoot } from "react-dom/client";
import { AuthKitProvider } from "@workos-inc/authkit-react";
import { QueryClient, QueryClientProvider, QueryCache, MutationCache } from "@tanstack/react-query";
import { RouterProvider } from "react-router-dom";
import { Toaster } from "sonner";
import { env, isDevAuth, isEntraAuth } from "@/lib/env";
import { ErrorBoundary } from "@/components/common/ErrorBoundary";
import { generateCorrelationId, logError } from "@/lib/logger";
import { initSentry } from "@/lib/sentry";
import { initAppInsights } from "@/lib/appInsights";
import { router } from "./router";

// Initialise observability providers before the first render.
// Each is a no-op when its env var is not set (e.g. local development).
if (env.VITE_SENTRY_DSN) {
  initSentry(env.VITE_SENTRY_DSN, env.VITE_APP_VERSION);
}
if (env.VITE_APPINSIGHTS_CONNECTION_STRING) {
  initAppInsights(env.VITE_APPINSIGHTS_CONNECTION_STRING, env.VITE_APP_VERSION);
}
import "./index.css";

// F-907: axe-core accessibility audit in development mode
if (import.meta.env.DEV) {
  void (async () => {
    const [axe, React, ReactDOM] = await Promise.all([
      import("@axe-core/react"),
      import("react"),
      import("react-dom"),
    ]);
    axe.default(React, ReactDOM, 1000);
  })();
}

const queryClient = new QueryClient({
  queryCache: new QueryCache({
    onError: (error, query) => {
      logError({
        correlationId: generateCorrelationId(),
        message: error.message,
        stack: error.stack,
        context: `QueryCache[${String(query.queryKey[0] ?? "unknown")}]`,
      });
    },
  }),
  mutationCache: new MutationCache({
    onError: (error, _variables, _context, mutation) => {
      logError({
        correlationId: generateCorrelationId(),
        message: error.message,
        stack: error.stack,
        context: `MutationCache[${mutation.options.mutationKey?.join(".") ?? "unknown"}]`,
      });
    },
  }),
  defaultOptions: {
    queries: {
      retry: 1,
      staleTime: 30_000,
    },
  },
});

const rootEl = document.getElementById("root");
if (!rootEl) throw new Error("Root element not found");

const app = (
  <ErrorBoundary>
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
      <Toaster richColors closeButton position="bottom-right" />
    </QueryClientProvider>
  </ErrorBoundary>
);

async function bootstrap() {
  if (isEntraAuth) {
    const { msalInstance } = await import("@/lib/msal");
    await msalInstance.initialize();
    // Process any redirect response (e.g., after loginRedirect completes).
    await msalInstance.handleRedirectPromise();
    // No MsalProvider needed — all components use the msalInstance singleton directly.
    createRoot(rootEl!).render(app);
  } else if (isDevAuth) {
    createRoot(rootEl!).render(app);
  } else {
    createRoot(rootEl!).render(
      <AuthKitProvider clientId={env.VITE_WORKOS_CLIENT_ID!} redirectUri={env.VITE_WORKOS_REDIRECT_URI!}>
        {app}
      </AuthKitProvider>,
    );
  }
}

void bootstrap();
