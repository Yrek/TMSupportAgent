import { useParams, Link, useNavigate } from "react-router-dom";
import { ArrowLeft, AlertTriangle, CheckCircle2, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { AdminShell } from "@/components/layout/AdminShell";
import { useAdminOrg, useSuspendOrg, useUnsuspendOrg, useAdminDeleteOrg } from "@/api/admin";
import { Skeleton } from "@/components/ui/skeleton";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { ConfirmDialog } from "@/components/common/ConfirmDialog";
import { useState } from "react";
import { usePageTitle } from "@/hooks/usePageTitle";

export function AdminOrgDetailPage() {
  const { orgId } = useParams<{ orgId: string }>();
  const navigate = useNavigate();
  const { data: org, isLoading } = useAdminOrg(orgId!);
  const suspend   = useSuspendOrg(orgId!);
  const unsuspend = useUnsuspendOrg(orgId!);
  const deleteOrg = useAdminDeleteOrg(orgId!);

  const [confirmDelete, setConfirmDelete] = useState(false);

  usePageTitle(org ? `Admin — ${org.name}` : "Admin — Org");

  if (isLoading) {
    return (
      <AdminShell>
        <div className="mx-auto max-w-3xl space-y-4 p-6">
          <Skeleton className="h-8 w-48" />
          <Skeleton className="h-32 w-full" />
        </div>
      </AdminShell>
    );
  }

  if (!org) {
    return (
      <AdminShell>
        <div className="mx-auto max-w-3xl p-6 text-center text-muted-foreground">
          Organization not found.
        </div>
      </AdminShell>
    );
  }

  async function handleSuspend() {
    try {
      await suspend.mutateAsync();
      toast.success(`${org!.name} suspended`);
    } catch {
      toast.error("Failed to suspend organization");
    }
  }

  async function handleUnsuspend() {
    try {
      await unsuspend.mutateAsync();
      toast.success(`${org!.name} unsuspended`);
    } catch {
      toast.error("Failed to unsuspend organization");
    }
  }

  async function handleDelete() {
    try {
      await deleteOrg.mutateAsync();
      toast.success("Organization deleted");
      navigate("/admin/orgs");
    } catch {
      toast.error("Failed to delete organization");
    }
  }

  return (
    <AdminShell>
      <div className="mx-auto max-w-3xl space-y-6 p-6">
        <Link
          to="/admin/orgs"
          className="flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground"
        >
          <ArrowLeft className="h-4 w-4" />
          All organizations
        </Link>

        <div className="flex items-start justify-between">
          <div>
            <div className="flex items-center gap-2">
              <h1 className="text-2xl font-bold">{org.name}</h1>
              {org.isSuspended && <Badge variant="destructive">Suspended</Badge>}
            </div>
            <p className="mt-1 text-sm text-muted-foreground">/{org.slug}</p>
          </div>
        </div>

        {/* Stats */}
        <div className="grid gap-4 sm:grid-cols-3">
          <div className="rounded-lg border p-4">
            <p className="text-sm text-muted-foreground">Members</p>
            <p className="mt-1 text-2xl font-bold">{org.memberCount}</p>
          </div>
          <div className="rounded-lg border p-4">
            <p className="text-sm text-muted-foreground">Jobs</p>
            <p className="mt-1 text-2xl font-bold">{org.jobCount}</p>
          </div>
          <div className="rounded-lg border p-4">
            <p className="text-sm text-muted-foreground">Created</p>
            <p className="mt-1 text-sm font-medium">{new Date(org.createdAt).toLocaleDateString()}</p>
          </div>
        </div>

        {/* Suspension info */}
        {org.isSuspended && org.suspendedAt && (
          <div className="flex items-center gap-3 rounded-lg border border-destructive/30 bg-destructive/5 p-4">
            <AlertTriangle className="h-5 w-5 text-destructive shrink-0" />
            <div>
              <p className="font-medium text-destructive">Suspended</p>
              <p className="text-sm text-muted-foreground">
                Since {new Date(org.suspendedAt).toLocaleString()}. Members cannot access the platform.
              </p>
            </div>
          </div>
        )}

        {/* Actions */}
        <div className="space-y-3 rounded-lg border p-4">
          <h2 className="font-semibold">Actions</h2>
          <div className="flex flex-wrap gap-3">
            {org.isSuspended ? (
              <Button
                variant="outline"
                onClick={handleUnsuspend}
                disabled={unsuspend.isPending}
              >
                <CheckCircle2 className="mr-2 h-4 w-4 text-green-500" />
                Unsuspend organization
              </Button>
            ) : (
              <Button
                variant="outline"
                onClick={handleSuspend}
                disabled={suspend.isPending}
              >
                <AlertTriangle className="mr-2 h-4 w-4 text-amber-500" />
                Suspend organization
              </Button>
            )}

            <Button
              variant="destructive"
              onClick={() => setConfirmDelete(true)}
            >
              <Trash2 className="mr-2 h-4 w-4" />
              Delete organization
            </Button>
          </div>
        </div>
      </div>

      <ConfirmDialog
        open={confirmDelete}
        onOpenChange={setConfirmDelete}
        title="Delete organization"
        description={`Permanently delete "${org.name}"? All jobs, threats, and member data will be lost. This cannot be undone.`}
        confirmLabel="Delete"
        confirmVariant="destructive"
        onConfirm={handleDelete}
        isLoading={deleteOrg.isPending}
      />
    </AdminShell>
  );
}
