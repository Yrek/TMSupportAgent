import { Link, Navigate } from "react-router-dom";
import { Building2, ArrowRight } from "lucide-react";
import { useSession } from "@/api/auth";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";

export function OrgPickerPage() {
  const { data: session, isLoading } = useSession();

  // If user has exactly one org, redirect directly to its dashboard
  if (!isLoading && session?.orgs.length === 1) {
    const orgId = session.orgs[0]?.id;
    if (orgId) return <Navigate to={`/orgs/${orgId}/jobs`} replace />;
  }

  return (
    <div className="flex min-h-screen flex-col items-center justify-center gap-8 p-8">
      <div className="w-full max-w-md space-y-6">
        <div className="text-center">
          <h1 className="text-2xl font-bold">Choose organisation</h1>
          <p className="mt-1 text-sm text-muted-foreground">
            Select an organisation you are a member of.
          </p>
        </div>

        {isLoading ? (
          <div className="space-y-2">
            <Skeleton className="h-14 w-full" />
            <Skeleton className="h-14 w-full" />
          </div>
        ) : !session?.orgs.length ? (
          /* Empty state — authenticated but no mapped org memberships */
          <div className="rounded-lg border border-dashed p-8 text-center">
            <Building2 className="mx-auto mb-3 h-10 w-10 text-muted-foreground" />
            <h2 className="font-semibold">No organization access</h2>
            <p className="mt-1 text-sm text-muted-foreground">
              Your account is signed in, but not mapped to an organization yet. Contact a platform
              admin or your org admin to be added.
            </p>
          </div>
        ) : (
          <div className="space-y-2">
            {session.orgs.map((org) => (
              <Link
                key={org.id}
                to={`/orgs/${org.id}/jobs`}
                className="flex items-center gap-3 rounded-lg border p-4 transition-colors hover:bg-muted"
              >
                <div className="flex h-10 w-10 items-center justify-center rounded-full bg-primary/10 text-primary font-semibold text-sm">
                  {org.name.charAt(0).toUpperCase()}
                </div>
                <div className="flex-1 min-w-0">
                  <div className="font-medium truncate">{org.name}</div>
                  <div className="text-xs text-muted-foreground">{org.slug}</div>
                </div>
                <Badge variant={org.role === "owner" ? "default" : "secondary"}>
                  {org.role === "owner" ? "Owner" : "Member"}
                </Badge>
                <ArrowRight className="h-4 w-4 text-muted-foreground" />
              </Link>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
