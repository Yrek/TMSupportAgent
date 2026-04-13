import { useState } from "react";
import { format } from "date-fns";
import { usePageTitle } from "@/hooks/usePageTitle";
import { useMe, useDeleteAccount } from "@/api/me";
import { AppShell } from "@/components/layout/AppShell";
import { ConfirmDialog } from "@/components/common/ConfirmDialog";
import { Button } from "@/components/ui/button";
import { Separator } from "@/components/ui/separator";
import { Skeleton } from "@/components/ui/skeleton";

export function ProfilePage() {
  usePageTitle("My Profile");
  const { data: me, isLoading } = useMe();
  const deleteAccount = useDeleteAccount();
  const [showDeleteDialog, setShowDeleteDialog] = useState(false);
  const [secondConfirm, setSecondConfirm] = useState(false);

  async function handleDelete() {
    if (!secondConfirm) {
      setSecondConfirm(true);
      return;
    }
    await deleteAccount.mutateAsync();
  }

  if (isLoading) {
    return (
      <AppShell>
        <div className="mx-auto max-w-md p-6 space-y-4">
          <Skeleton className="h-8 w-32" />
          <Skeleton className="h-6 w-64" />
          <Skeleton className="h-6 w-48" />
        </div>
      </AppShell>
    );
  }

  return (
    <AppShell>
      <div className="mx-auto max-w-md space-y-8 p-6">
        <h1 className="text-2xl font-bold">Profile</h1>

        <div className="space-y-3">
          <div>
            <p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">Internal ID</p>
            <p className="mt-0.5 font-mono text-sm">{me?.id}</p>
          </div>
          <div>
            <p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">WorkOS User ID</p>
            <p className="mt-0.5 font-mono text-sm">{me?.workosUserId}</p>
          </div>
          {me?.createdAt && (
            <div>
              <p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">Account created</p>
              <p className="mt-0.5 text-sm">
                {format(new Date(me.createdAt), "PPP")}
              </p>
            </div>
          )}
        </div>

        <Separator />

        <div className="rounded-lg border border-destructive/30 p-4 space-y-3">
          <h2 className="font-semibold text-destructive">Delete account</h2>
          <p className="text-sm text-muted-foreground">
            This is irreversible. All your data will be permanently deleted.
          </p>
          <Button variant="destructive" size="sm" onClick={() => setShowDeleteDialog(true)}>
            Delete my account
          </Button>
        </div>
      </div>

      <ConfirmDialog
        open={showDeleteDialog}
        onOpenChange={(open) => {
          setShowDeleteDialog(open);
          if (!open) setSecondConfirm(false);
        }}
        title="Delete account"
        description={
          secondConfirm
            ? "This is your final confirmation. Your account and all associated data will be permanently deleted."
            : "Are you sure you want to delete your account? This is irreversible."
        }
        confirmLabel={secondConfirm ? "Yes, delete permanently" : "I understand, continue"}
        confirmVariant="destructive"
        onConfirm={handleDelete}
        isLoading={deleteAccount.isPending}
      />
    </AppShell>
  );
}
