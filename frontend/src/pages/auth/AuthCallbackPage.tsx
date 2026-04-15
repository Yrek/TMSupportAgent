import { useAuth } from "@workos-inc/authkit-react";
import { useEffect } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { ShieldCheck } from "lucide-react";

function isInternalPath(path: string | null): boolean {
  if (!path) return false;
  return path.startsWith("/") && !path.startsWith("//") && !path.includes(":");
}

export function AuthCallbackPage() {
  const { user, isLoading } = useAuth();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();

  useEffect(() => {
    if (!isLoading && user) {
      const returnTo = searchParams.get("return_to");
      const dest = isInternalPath(returnTo) ? returnTo! : "/";
      navigate(dest, { replace: true });
      return;
    }

    // Callback finished but no authenticated user was established.
    // Redirect to login with an explicit error instead of showing an infinite spinner.
    if (!isLoading && !user) {
      navigate("/login?error=auth_callback_failed", { replace: true });
    }
  }, [user, isLoading, navigate, searchParams]);

  return (
    <div className="flex min-h-screen flex-col items-center justify-center gap-4">
      <ShieldCheck className="h-10 w-10 text-primary" />
      <p className="text-muted-foreground">Completing sign-in…</p>
      <div className="h-6 w-6 animate-spin rounded-full border-4 border-primary border-t-transparent" />
    </div>
  );
}
