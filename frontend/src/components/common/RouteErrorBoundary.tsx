import { useEffect, useId } from "react";
import { useRouteError, isRouteErrorResponse, useNavigate } from "react-router-dom";
import { AlertTriangle } from "lucide-react";
import { Button } from "@/components/ui/button";
import { generateCorrelationId, logError } from "@/lib/logger";

/**
 * Used as React Router's `errorElement` on the root route.
 * Catches errors thrown during rendering, data loading, and actions.
 * Logs every error with a correlation ID before rendering the fallback UI.
 */
export function RouteErrorBoundary() {
  const error = useRouteError();
  const navigate = useNavigate();
  // useId is stable across renders for the same component instance
  const correlationId = useId().replace(/:/g, "").slice(0, 8) + "-" + generateCorrelationId().slice(0, 8);

  useEffect(() => {
    const message = isRouteErrorResponse(error)
      ? `HTTP ${error.status}: ${error.statusText}`
      : error instanceof Error
        ? error.message
        : String(error);

    logError({
      correlationId,
      message,
      stack: error instanceof Error ? error.stack : undefined,
      context: "RouteErrorBoundary",
    });
  }, [correlationId, error]);

  const isNotFound = isRouteErrorResponse(error) && error.status === 404;

  if (isNotFound) {
    return (
      <div className="flex min-h-screen flex-col items-center justify-center gap-6 p-8 text-center">
        <AlertTriangle className="h-16 w-16 text-muted-foreground" />
        <div>
          <h1 className="text-2xl font-bold">Page not found</h1>
          <p className="mt-2 text-muted-foreground">
            The page you&apos;re looking for doesn&apos;t exist.
          </p>
        </div>
        <Button onClick={() => navigate("/orgs")}>Go to dashboard</Button>
      </div>
    );
  }

  return (
    <div className="flex min-h-screen flex-col items-center justify-center gap-6 p-8 text-center">
      <AlertTriangle className="h-16 w-16 text-destructive" />
      <div>
        <h1 className="text-2xl font-bold">Something went wrong</h1>
        <p className="mt-2 text-muted-foreground">
          An unexpected error occurred. Please try again or go back to the dashboard.
        </p>
        <p className="mt-2 font-mono text-xs text-muted-foreground">Reference: {correlationId}</p>
      </div>
      <div className="flex gap-3">
        <Button variant="outline" onClick={() => navigate(-1)}>
          Go back
        </Button>
        <Button onClick={() => navigate("/orgs")}>Go to dashboard</Button>
      </div>
    </div>
  );
}
