import { useState } from "react";
import { useParams, NavLink } from "react-router-dom";
import { usePageTitle } from "@/hooks/usePageTitle";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { toast } from "sonner";
import { useOrg, useUpdateOrg, useDeleteOrg } from "@/api/orgs";
import { useOrgContext } from "@/hooks/useOrgContext";
import { AppShell } from "@/components/layout/AppShell";
import { ConfirmDialog } from "@/components/common/ConfirmDialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Separator } from "@/components/ui/separator";
import { Skeleton } from "@/components/ui/skeleton";
import { cn } from "@/lib/utils";

const nameSchema = z.object({ name: z.string().min(2).max(100) });
type NameForm = z.infer<typeof nameSchema>;

export function OrgSettingsPage() {
  usePageTitle("Settings");
  const { orgId } = useParams<{ orgId: string }>();
  const { isOwner } = useOrgContext();
  const { data: org, isLoading } = useOrg(orgId!);
  const updateOrg = useUpdateOrg(orgId!);
  const deleteOrg = useDeleteOrg(orgId!);

  const [showDeleteDialog, setShowDeleteDialog] = useState(false);
  const [confirmName, setConfirmName] = useState("");

  const { register, handleSubmit, formState: { errors, isSubmitting } } = useForm<NameForm>({
    resolver: zodResolver(nameSchema),
    ...(org ? { values: { name: org.name } } : {}),
  });

  async function onSaveName(values: NameForm) {
    try {
      await updateOrg.mutateAsync({ name: values.name });
      toast.success("Organisation name updated");
    } catch {
      toast.error("Failed to update name");
    }
  }

  async function handleDelete() {
    try {
      await deleteOrg.mutateAsync();
    } catch {
      toast.error("Failed to delete organisation");
      setShowDeleteDialog(false);
    }
  }

  if (isLoading) {
    return (
      <AppShell>
        <div className="mx-auto max-w-2xl p-6 space-y-4">
          <Skeleton className="h-8 w-48" />
          <Skeleton className="h-12 w-full" />
        </div>
      </AppShell>
    );
  }

  const settingsNavClass = ({ isActive }: { isActive: boolean }) =>
    cn(
      "border-b-2 px-4 pb-2 text-sm transition-colors shrink-0",
      isActive
        ? "border-primary text-primary font-medium"
        : "border-transparent text-muted-foreground hover:text-foreground",
    );

  return (
    <AppShell>
      <div className="mx-auto max-w-2xl space-y-8 p-6">
        <h1 className="text-2xl font-bold">Settings</h1>

        {isOwner && (
          <div className="flex gap-1 overflow-x-auto border-b">
            <NavLink to={`/orgs/${orgId!}/settings`} end className={settingsNavClass}>General</NavLink>
            <NavLink to={`/orgs/${orgId!}/settings/members`} className={settingsNavClass}>Members</NavLink>
            <NavLink to={`/orgs/${orgId!}/settings/idp`} className={settingsNavClass}>Enterprise SSO</NavLink>
            <NavLink to={`/orgs/${orgId!}/settings/audit`} className={settingsNavClass}>Audit log</NavLink>
          </div>
        )}

        {/* Name */}
        <form onSubmit={handleSubmit(onSaveName)} className="space-y-4">
          <div className="space-y-1.5">
            <Label htmlFor="orgName">Organisation name</Label>
            <Input id="orgName" {...register("name")} disabled={!isOwner} />
            {errors.name && <p className="text-sm text-destructive">{errors.name.message}</p>}
          </div>
          {isOwner && (
            <Button type="submit" disabled={isSubmitting}>
              {isSubmitting ? "Saving…" : "Save"}
            </Button>
          )}
        </form>

        <Separator />

        {/* Danger zone */}
        {isOwner && (
          <div className="rounded-lg border border-destructive/30 p-4 space-y-3">
            <h2 className="font-semibold text-destructive">Danger zone</h2>
            <p className="text-sm text-muted-foreground">
              Deleting this organisation is permanent and cannot be undone.
            </p>
            <Button variant="destructive" onClick={() => setShowDeleteDialog(true)}>
              Delete organisation
            </Button>
          </div>
        )}
      </div>

      <ConfirmDialog
        open={showDeleteDialog}
        onOpenChange={setShowDeleteDialog}
        title="Delete organisation"
        description={`Type the organisation name "${org?.name}" to confirm deletion. This is irreversible.`}
        confirmLabel="Delete permanently"
        confirmVariant="destructive"
        onConfirm={handleDelete}
        isLoading={deleteOrg.isPending}
      >
        <Input
          value={confirmName}
          onChange={(e) => setConfirmName(e.target.value)}
          placeholder={org?.name}
        />
      </ConfirmDialog>
    </AppShell>
  );
}
