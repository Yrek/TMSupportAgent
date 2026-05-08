import { useAuth } from "@workos-inc/authkit-react";
import { Navigate, useLocation } from "react-router-dom";
import { setAccessToken, registerSilentRefresh, hasAccessToken } from "@/api/client";
import { isDevAuth } from "@/lib/env";
import { useEffect, useState } from "react";

interface RequireAuthProps {
  children: React.ReactNode;
}

// WorkOS variant — only rendered when AuthKitProvider is in the tree (isDevAuth=false).
function RequireAuthWorkOS({ children }: RequireAuthProps) {
  const { user, getAccessToken, isLoading } = useAuth();
  const location = useLocation();
  const [tokenReady, setTokenReady] = useState(false);
  const [tokenMissing, setTokenMissing] = useState(false);

  // Keep in-memory token in sync with WorkOS AuthKit.
  useEffect(() => {
    let cancelled = false;
    const userId = user?.id ?? null;

    if (userId) {
      setTokenReady(false);
      setTokenMissing(false);

      void getAccessToken().then((token) => {
        if (cancelled) return;
        setAccessToken(token ?? null);
        setTokenMissing(!token);
        setTokenReady(true);
      });

      registerSilentRefresh(async () => {
        const t = await getAccessToken();
        return t ?? null;
      });
    } else {
      setAccessToken(null);
      setTokenMissing(false);
      setTokenReady(false);
    }

    return () => {
      cancelled = true;
    };
  }, [user?.id, getAccessToken]);

  if (isLoading || (!!user && !tokenReady)) {
    return (
      <div className="flex min-h-screen items-center justify-center">
        <div className="h-8 w-8 animate-spin rounded-full border-4 border-primary border-t-transparent" />
      </div>
    );
  }

  if (!user || tokenMissing) {
    const returnTo = location.pathname + location.search;
    const isInternal = returnTo.startsWith("/") && !returnTo.startsWith("//");
    const error = tokenMissing ? "&error=missing_api_token" : "";
    return (
      <Navigate
        to={`/login${isInternal ? `?return_to=${encodeURIComponent(returnTo)}${error}` : error ? `?${error.slice(1)}` : ""}`}
        replace
      />
    );
  }

  return <>{children}</>;
}

// Dev auth variant — checks in-memory token set by DevLoginPage.
function RequireAuthDev({ children }: RequireAuthProps) {
  const location = useLocation();
  const [ready, setReady] = useState(hasAccessToken);

  useEffect(() => {
    setReady(hasAccessToken());
  });

  if (!ready) {
    const returnTo = location.pathname + location.search;
    const isInternal = returnTo.startsWith("/") && !returnTo.startsWith("//");
    return (
      <Navigate
        to={`/login${isInternal ? `?return_to=${encodeURIComponent(returnTo)}` : ""}`}
        replace
      />
    );
  }

  return <>{children}</>;
}

export function RequireAuth({ children }: RequireAuthProps) {
  return isDevAuth
    ? <RequireAuthDev>{children}</RequireAuthDev>
    : <RequireAuthWorkOS>{children}</RequireAuthWorkOS>;
}
