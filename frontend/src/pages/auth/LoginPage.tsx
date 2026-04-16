import { useSearchParams, Navigate } from "react-router-dom";
import { useAuth } from "@workos-inc/authkit-react";
import { ShieldCheck } from "lucide-react";
import { Button } from "@/components/ui/button";

function isInternalPath(path: string | null): boolean {
  if (!path) return false;
  // Must start with / but not // (protocol-relative) and not contain :
  return path.startsWith("/") && !path.startsWith("//") && !path.includes(":");
}

export function LoginPage() {
  const { user, isLoading, signIn } = useAuth();
  const [searchParams] = useSearchParams();
  const returnTo = searchParams.get("return_to");
  const error = searchParams.get("error");

  if (!isLoading && user && !error) {
    const dest = isInternalPath(returnTo) && returnTo ? returnTo : "/";
    return <Navigate to={dest} replace />;
  }

  function handleSignIn() {
    void signIn();
  }

  return (
    <main className="flex min-h-screen flex-col items-center justify-center gap-8 p-8">
      <div className="flex flex-col items-center gap-4">
        <ShieldCheck className="h-12 w-12 text-primary" />
        <h1 className="text-3xl font-bold">Threat Modeling Agent</h1>
        <p className="max-w-sm text-center text-muted-foreground">
          AI-powered threat modeling for your architecture. Sign in to get started.
        </p>
        {error === "missing_api_token" ? (
          <p className="max-w-md text-center text-sm text-destructive">
            Signed in to WorkOS, but no API token was issued. Check WorkOS app scopes/audience and try sign in again.
          </p>
        ) : null}
        {error === "auth_callback_failed" ? (
          <p className="max-w-md text-center text-sm text-destructive">
            Sign-in callback failed. Please try signing in again.
          </p>
        ) : null}
      </div>
      <Button size="lg" onClick={handleSignIn} disabled={isLoading}>
        {isLoading ? "Loading..." : "Sign in"}
      </Button>
    </main>
  );
}
