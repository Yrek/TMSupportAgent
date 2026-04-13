import { Link, useRouteError, isRouteErrorResponse } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { AlertTriangle } from "lucide-react";

export function ErrorPage() {
  const error = useRouteError();

  let message = "An unexpected error occurred.";
  if (isRouteErrorResponse(error)) {
    message = error.statusText ?? String(error.status);
  } else if (error instanceof Error) {
    message = "Something went wrong. Please try again.";
  }

  return (
    <div className="flex min-h-screen flex-col items-center justify-center gap-6 p-8 text-center">
      <AlertTriangle className="h-16 w-16 text-destructive" />
      <div>
        <h1 className="text-3xl font-bold">Something went wrong</h1>
        <p className="mt-2 text-muted-foreground">{message}</p>
      </div>
      <Button asChild variant="outline">
        <Link to="/">Go home</Link>
      </Button>
    </div>
  );
}
