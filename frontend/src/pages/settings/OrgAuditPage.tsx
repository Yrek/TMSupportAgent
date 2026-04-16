import { useState } from "react";
import { useParams } from "react-router-dom";
import { ChevronLeft, ChevronRight } from "lucide-react";
import { AppShell } from "@/components/layout/AppShell";
import { useOrgAuditLog } from "@/api/orgs";
import { Skeleton } from "@/components/ui/skeleton";
import { usePageTitle } from "@/hooks/usePageTitle";
import { requiredParam } from "@/lib/requiredParam";

export function OrgAuditPage() {
  usePageTitle("Audit log");
  const params = useParams<{ orgId: string }>();
  const orgId = requiredParam(params.orgId, "orgId");
  const [page, setPage] = useState(1);
  const { data, isLoading } = useOrgAuditLog(orgId, page);

  const entries = data?.data ?? [];
  const pagination = data?.pagination;

  function eventLabel(eventType: string) {
    return eventType
      .replace(/\./g, " › ")
      .replace(/_/g, " ");
  }

  return (
    <AppShell>
      <div className="mx-auto max-w-4xl space-y-6 p-6">
        <div className="flex items-center justify-between">
          <h1 className="text-2xl font-bold">Audit log</h1>
          {pagination && (
            <span className="text-sm text-muted-foreground">
              {pagination.total.toLocaleString()} events
            </span>
          )}
        </div>

        <p className="text-sm text-muted-foreground">
          Immutable record of all significant actions taken within this organization.
        </p>

        {isLoading ? (
          <div className="space-y-2">
            {[1, 2, 3, 4, 5].map((i) => <Skeleton key={i} className="h-12 w-full" />)}
          </div>
        ) : !entries.length ? (
          <div className="rounded-lg border border-dashed p-10 text-center text-muted-foreground">
            No audit events recorded yet.
          </div>
        ) : (
          <div className="rounded-lg border divide-y text-sm">
            <div className="grid grid-cols-[1fr_auto_auto] gap-4 px-4 py-2 bg-muted/50 text-xs font-medium text-muted-foreground">
              <span>Event</span>
              <span>User</span>
              <span>Time</span>
            </div>
            {entries.map((e) => (
              <div key={e.id} className="grid grid-cols-[1fr_auto_auto] gap-4 px-4 py-3 items-center">
                <div>
                  <span className="font-medium">{eventLabel(e.eventType)}</span>
                  {e.resourceType && (
                    <span className="ml-2 text-xs text-muted-foreground">
                      {e.resourceType}{e.resourceId ? ` · ${e.resourceId.slice(0, 8)}…` : ""}
                    </span>
                  )}
                </div>
                <span className="text-xs text-muted-foreground font-mono">
                  {e.userId ? e.userId.slice(0, 8) + "…" : "—"}
                </span>
                <span className="text-xs text-muted-foreground whitespace-nowrap">
                  {new Date(e.createdAt).toLocaleString()}
                </span>
              </div>
            ))}
          </div>
        )}

        {pagination && pagination.totalPages > 1 && (
          <div className="flex items-center justify-center gap-3">
            <button
              onClick={() => setPage((p) => Math.max(1, p - 1))}
              disabled={page === 1}
              className="rounded-md border p-1.5 disabled:opacity-40"
            >
              <ChevronLeft className="h-4 w-4" />
            </button>
            <span className="text-sm text-muted-foreground">
              Page {page} of {pagination.totalPages}
            </span>
            <button
              onClick={() => setPage((p) => Math.min(pagination.totalPages, p + 1))}
              disabled={page === pagination.totalPages}
              className="rounded-md border p-1.5 disabled:opacity-40"
            >
              <ChevronRight className="h-4 w-4" />
            </button>
          </div>
        )}
      </div>
    </AppShell>
  );
}
