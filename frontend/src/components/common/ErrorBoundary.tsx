import { Component, type ReactNode, type ErrorInfo } from "react";
import { AlertTriangle } from "lucide-react";
import { Button } from "@/components/ui/button";
import { generateCorrelationId, logError } from "@/lib/logger";

interface Props {
  children: ReactNode;
  fallback?: ReactNode;
}

interface State {
  hasError: boolean;
  correlationId: string | null;
}

export class ErrorBoundary extends Component<Props, State> {
  constructor(props: Props) {
    super(props);
    this.state = { hasError: false, correlationId: null };
  }

  static getDerivedStateFromError(): State {
    // Generate the correlation ID synchronously so it is available to both
    // componentDidCatch (for logging) and render (to show to the user).
    return { hasError: true, correlationId: generateCorrelationId() };
  }

  componentDidCatch(error: Error, errorInfo: ErrorInfo) {
    logError({
      correlationId: this.state.correlationId ?? "unknown",
      message: error.message,
      stack: error.stack,
      componentStack: errorInfo.componentStack ?? undefined,
      context: "ErrorBoundary",
    });
  }

  render() {
    if (this.state.hasError) {
      if (this.props.fallback) return this.props.fallback;

      return (
        <div className="flex min-h-screen flex-col items-center justify-center gap-6 p-8 text-center">
          <AlertTriangle className="h-16 w-16 text-destructive" />
          <div>
            <h1 className="text-2xl font-bold">Something went wrong</h1>
            <p className="mt-2 text-muted-foreground">
              An unexpected error occurred. Please refresh the page to try again.
            </p>
            {this.state.correlationId && (
              <p className="mt-2 font-mono text-xs text-muted-foreground">
                Reference: {this.state.correlationId}
              </p>
            )}
          </div>
          <Button onClick={() => window.location.reload()}>Refresh page</Button>
        </div>
      );
    }

    return this.props.children;
  }
}
