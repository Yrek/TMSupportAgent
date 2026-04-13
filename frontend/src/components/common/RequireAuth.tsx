import { useAuth } from "@workos-inc/authkit-react";
import { Navigate, useLocation } from "react-router-dom";
import { setAccessToken, registerSilentRefresh } from "@/api/client";
import { useEffect } from "react";

interface RequireAuthProps {
  children: React.ReactNode;
}

export function RequireAuth({ children }: RequireAuthProps) {
  const { user, getAccessToken, isLoading } = useAuth();
  const location = useLocation();

  // Keep in-memory token in sync with WorkOS AuthKit
  useEffect(() => {
    if (user) {
      void getAccessToken().then((token) => {
        setAccessToken(token ?? null);
      });
      registerSilentRefresh(async () => {
        const t = await getAccessToken();
        return t ?? null;
      });
    } else {
      setAccessToken(null);
    }
  }, [user, getAccessToken]);

  if (isLoading) {
    return (
      <div className="flex min-h-screen items-center justify-center">
        <div className="h-8 w-8 animate-spin rounded-full border-4 border-primary border-t-transparent" />
      </div>
    );
  }

  if (!user) {
    // Validate that the return_to path is internal before using it
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
