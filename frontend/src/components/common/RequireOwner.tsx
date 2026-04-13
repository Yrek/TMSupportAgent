import { useOrgContext } from "@/hooks/useOrgContext";
import { ShieldAlert } from "lucide-react";

interface RequireOwnerProps {
  children: React.ReactNode;
}

export function RequireOwner({ children }: RequireOwnerProps) {
  const { isOwner } = useOrgContext();

  if (!isOwner) {
    return (
      <div className="flex flex-col items-center justify-center gap-4 py-24 text-center">
        <ShieldAlert className="h-12 w-12 text-muted-foreground" />
        <h2 className="text-xl font-semibold">Owner access required</h2>
        <p className="max-w-sm text-muted-foreground">
          This section is only accessible to organisation owners.
        </p>
      </div>
    );
  }

  return <>{children}</>;
}
