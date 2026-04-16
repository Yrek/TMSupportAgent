import { Navigate } from "react-router-dom";
import { useSession } from "@/api/auth";
import { Spinner } from "@/components/common/Spinner";
import { Button } from "@/components/ui/button";

interface RequirePlatformAdminProps {
  children: React.ReactNode;
}

export function RequirePlatformAdmin({ children }: RequirePlatformAdminProps) {
  const { data: session, isLoading, isError, refetch } = useSession();

  if (isLoading) {
    return (
      <div className="flex min-h-screen items-center justify-center">
        <Spinner />
      </div>
    );
  }

  if (isError) {
    return (
      <div className="flex min-h-screen flex-col items-center justify-center gap-3 p-6 text-center">
        <h1 className="text-xl font-semibold">Unable to verify admin session</h1>
        <p className="text-sm text-muted-foreground">
          The API session check failed. Please retry or sign in again.
        </p>
        <div className="flex gap-2">
          <Button onClick={() => void refetch()}>Retry</Button>
          <Button variant="outline" onClick={() => (window.location.href = "/login")}>
            Sign in again
          </Button>
        </div>
      </div>
    );
  }

  if (!session?.isPlatformAdmin) {
    return <Navigate to="/unauthorized" replace />;
  }

  return <>{children}</>;
}
