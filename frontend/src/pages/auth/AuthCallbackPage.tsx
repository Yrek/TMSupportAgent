import { useAuth } from "@workos-inc/authkit-react";
import { useEffect } from "react";
import { Navigate, useNavigate, useSearchParams } from "react-router-dom";
import { ShieldCheck } from "lucide-react";
import { isDevAuth, isEntraAuth } from "@/lib/env";

function isInternalPath(path: string | null): boolean {
  if (!path) return false;
  return path.startsWith("/") && !path.startsWith("//") && !path.includes(":");
}

const Spinner = () => (
  <div className="flex min-h-screen flex-col items-center justify-center gap-4">
    <ShieldCheck className="h-10 w-10 text-primary" />
    <p className="text-muted-foreground">Completing sign-in…</p>
    <div className="h-6 w-6 animate-spin rounded-full border-4 border-primary border-t-transparent" />
  </div>
);

function AuthCallbackWorkOS() {
  const { user, isLoading } = useAuth();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();

  useEffect(() => {
    if (!isLoading && user) {
      const returnTo = searchParams.get("return_to");
      const dest = isInternalPath(returnTo) && returnTo ? returnTo : "/";
      navigate(dest, { replace: true });
      return;
    }

    // Callback finished but no authenticated user was established.
    if (!isLoading && !user) {
      navigate("/login?error=auth_callback_failed", { replace: true });
    }
  }, [user, isLoading, navigate, searchParams]);

  return <Spinner />;
}

// Entra: MSAL processes the redirect during app bootstrap (handleRedirectPromise in main.tsx).
// By the time this component mounts, the account is already in MSAL's session storage.
function AuthCallbackEntra() {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();

  useEffect(() => {
    void (async () => {
      const { msalInstance } = await import("@/lib/msal");
      const accounts = msalInstance.getAllAccounts();
      if (accounts.length > 0) {
        const returnTo = searchParams.get("return_to");
        const dest = isInternalPath(returnTo) && returnTo ? returnTo : "/";
        navigate(dest, { replace: true });
      } else {
        navigate("/login?error=auth_callback_failed", { replace: true });
      }
    })();
  }, [navigate, searchParams]);

  return <Spinner />;
}

export function AuthCallbackPage() {
  if (isDevAuth) return <Navigate to="/login" replace />;
  if (isEntraAuth) return <AuthCallbackEntra />;
  return <AuthCallbackWorkOS />;
}
