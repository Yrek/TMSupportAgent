import { useSearchParams, Navigate, useNavigate } from "react-router-dom";
import { useAuth } from "@workos-inc/authkit-react";
import { ShieldCheck } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { isDevAuth, isEntraAuth } from "@/lib/env";
import { setAccessToken } from "@/api/client";
import { apiClient } from "@/api/client";
import { useState } from "react";

function isInternalPath(path: string | null): boolean {
  if (!path) return false;
  return path.startsWith("/") && !path.startsWith("//") && !path.includes(":");
}

function LoginPageWorkOS() {
  const { user, isLoading, signIn } = useAuth();
  const [searchParams] = useSearchParams();
  const returnTo = searchParams.get("return_to");
  const error = searchParams.get("error");

  if (!isLoading && user && !error) {
    const dest = isInternalPath(returnTo) && returnTo ? returnTo : "/";
    return <Navigate to={dest} replace />;
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
      <Button size="lg" onClick={() => { void signIn(); }} disabled={isLoading}>
        {isLoading ? "Loading..." : "Sign in"}
      </Button>
    </main>
  );
}

function LoginPageDev() {
  const [searchParams] = useSearchParams();
  const returnTo = searchParams.get("return_to");
  const navigate = useNavigate();
  const [email, setEmail] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setLoading(true);
    setError(null);
    try {
      const res = await apiClient.post<{ accessToken: string }>("/auth/dev-login", { email });
      setAccessToken(res.data.accessToken);
      const dest = isInternalPath(returnTo) && returnTo ? returnTo : "/";
      navigate(dest, { replace: true });
    } catch {
      setError("Login failed. Check the API is running with DevAuth:Enabled=true.");
    } finally {
      setLoading(false);
    }
  }

  return (
    <main className="flex min-h-screen flex-col items-center justify-center gap-8 p-8">
      <div className="flex flex-col items-center gap-4">
        <ShieldCheck className="h-12 w-12 text-primary" />
        <h1 className="text-3xl font-bold">Threat Modeling Agent</h1>
        <p className="max-w-sm text-center text-muted-foreground">
          Dev auth mode — enter any email to sign in locally.
        </p>
      </div>
      <form onSubmit={(e) => { void handleSubmit(e); }} className="flex w-full max-w-sm flex-col gap-3">
        <Input
          type="email"
          placeholder="you@example.com"
          value={email}
          onChange={(e) => { setEmail(e.target.value); }}
          required
          autoFocus
        />
        {error ? <p className="text-sm text-destructive">{error}</p> : null}
        <Button type="submit" size="lg" disabled={loading}>
          {loading ? "Signing in…" : "Sign in"}
        </Button>
      </form>
    </main>
  );
}

function LoginPageEntra() {
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function handleSignIn() {
    setLoading(true);
    setError(null);
    try {
      const { msalInstance, entraLoginRequest } = await import("@/lib/msal");
      await msalInstance.loginRedirect(entraLoginRequest);
    } catch {
      setError("Sign-in failed. Please try again.");
      setLoading(false);
    }
  }

  return (
    <main className="flex min-h-screen flex-col items-center justify-center gap-8 p-8">
      <div className="flex flex-col items-center gap-4">
        <ShieldCheck className="h-12 w-12 text-primary" />
        <h1 className="text-3xl font-bold">Threat Modeling Agent</h1>
        <p className="max-w-sm text-center text-muted-foreground">
          Sign in with your Microsoft account to get started.
        </p>
        {error ? <p className="max-w-md text-center text-sm text-destructive">{error}</p> : null}
      </div>
      <Button size="lg" onClick={() => { void handleSignIn(); }} disabled={loading}>
        {loading ? "Redirecting…" : "Sign in with Microsoft"}
      </Button>
    </main>
  );
}

export function LoginPage() {
  if (isDevAuth) return <LoginPageDev />;
  if (isEntraAuth) return <LoginPageEntra />;
  return <LoginPageWorkOS />;
}
