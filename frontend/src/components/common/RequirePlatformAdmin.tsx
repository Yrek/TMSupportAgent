import { Navigate } from "react-router-dom";
import { useSession } from "@/api/auth";
import { Spinner } from "@/components/common/Spinner";

interface RequirePlatformAdminProps {
  children: React.ReactNode;
}

export function RequirePlatformAdmin({ children }: RequirePlatformAdminProps) {
  const { data: session, isLoading } = useSession();

  if (isLoading) {
    return (
      <div className="flex min-h-screen items-center justify-center">
        <Spinner />
      </div>
    );
  }

  if (!session?.isPlatformAdmin) {
    return <Navigate to="/unauthorized" replace />;
  }

  return <>{children}</>;
}
