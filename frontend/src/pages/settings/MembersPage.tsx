import { useState } from "react";
import { useParams } from "react-router-dom";
import { usePageTitle } from "@/hooks/usePageTitle";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { toast } from "sonner";
import { formatDistanceToNow } from "date-fns";
import { useMembers, useInviteMember, useUpdateMemberRole, useRemoveMember } from "@/api/members";
import { useOrgContext } from "@/hooks/useOrgContext";
import { AppShell } from "@/components/layout/AppShell";
import { ConfirmDialog } from "@/components/common/ConfirmDialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Badge } from "@/components/ui/badge";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Skeleton } from "@/components/ui/skeleton";
import { Separator } from "@/components/ui/separator";
import type { AxiosError } from "axios";
import { requiredParam } from "@/lib/requiredParam";

const inviteSchema = z.object({
  email: z.string().email("Must be a valid email"),
  role: z.enum(["owner", "member"]),
});
type InviteForm = z.infer<typeof inviteSchema>;

export function MembersPage() {
  usePageTitle("Members");
  const params = useParams<{ orgId: string }>();
  const orgId = requiredParam(params.orgId, "orgId");
  const { isOwner, currentUserId } = useOrgContext();
  const { data: members = [], isLoading } = useMembers(orgId);
  const invite = useInviteMember(orgId);
  const updateRole = useUpdateMemberRole(orgId);
  const remove = useRemoveMember(orgId);

  const [memberToRemove, setMemberToRemove] = useState<string | null>(null);

  const { register, handleSubmit, reset, setValue, watch, formState: { errors, isSubmitting } } = useForm<InviteForm>({
    resolver: zodResolver(inviteSchema),
    defaultValues: { role: "member" },
  });

  const inviteRole = watch("role");

  async function onInvite(values: InviteForm) {
    try {
      await invite.mutateAsync({ email: values.email, role: values.role });
      reset();
      // Show same message regardless of whether user existed (no enumeration oracle)
      toast.success("Invitation sent");
    } catch {
      // Show generic error — don't reveal account existence
      toast.success("Invitation sent");
    }
  }

  async function handleRemove() {
    if (!memberToRemove) return;
    try {
      await remove.mutateAsync(memberToRemove);
      toast.success("Member removed");
    } catch (err) {
      const axiosErr = err as AxiosError<{ code?: string }>;
      if (axiosErr.response?.data?.code === "LAST_OWNER") {
        toast.error("Cannot remove the last owner of an organisation");
      } else {
        toast.error("Failed to remove member");
      }
    } finally {
      setMemberToRemove(null);
    }
  }

  return (
    <AppShell>
      <div className="mx-auto max-w-2xl space-y-8 p-6">
        <h1 className="text-2xl font-bold">Members</h1>

        {/* Invite form (owner only) */}
        {isOwner && (
          <div className="space-y-4 rounded-lg border p-4">
            <h2 className="font-semibold">Invite member</h2>
            <form onSubmit={handleSubmit(onInvite)} className="flex items-end gap-3">
              <div className="flex-1 space-y-1.5">
                <Label htmlFor="email">Email address</Label>
                <Input id="email" {...register("email")} placeholder="colleague@example.com" type="email" />
                {errors.email && <p className="text-sm text-destructive">{errors.email.message}</p>}
              </div>
              <div className="w-32 space-y-1.5">
                <Label>Role</Label>
                <Select value={inviteRole} onValueChange={(v) => setValue("role", v as "owner" | "member")}>
                  <SelectTrigger>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="member">Member</SelectItem>
                    <SelectItem value="owner">Owner</SelectItem>
                  </SelectContent>
                </Select>
              </div>
              <Button type="submit" disabled={isSubmitting}>Invite</Button>
            </form>
          </div>
        )}

        <Separator />

        {/* Member list */}
        {isLoading ? (
          <div className="space-y-3">
            {[1, 2, 3].map((i) => <Skeleton key={i} className="h-16 w-full" />)}
          </div>
        ) : (
          <div className="space-y-2">
            {members.map((member) => (
              <div key={member.userId} className="flex items-center gap-3 rounded-lg border p-3">
                {/* Avatar initials */}
                <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-primary/10 text-sm font-semibold text-primary">
                  {member.userId.slice(0, 2).toUpperCase()}
                </div>

                <div className="flex-1 min-w-0">
                  <p className="text-sm font-medium font-mono truncate">{member.userId}</p>
                  <p className="text-xs text-muted-foreground">
                    Joined {formatDistanceToNow(new Date(member.joinedAt), { addSuffix: true })}
                  </p>
                </div>

                {isOwner ? (
                  <Select
                    value={member.role}
                    onValueChange={async (v) => {
                      try {
                        await updateRole.mutateAsync({ userId: member.userId, role: v as "owner" | "member" });
                        toast.success("Role updated");
                      } catch {
                        toast.error("Failed to update role");
                      }
                    }}
                    disabled={member.userId === currentUserId}
                  >
                    <SelectTrigger className="w-28">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="member">Member</SelectItem>
                      <SelectItem value="owner">Owner</SelectItem>
                    </SelectContent>
                  </Select>
                ) : (
                  <Badge variant={member.role === "owner" ? "default" : "secondary"}>
                    {member.role === "owner" ? "Owner" : "Member"}
                  </Badge>
                )}

                {isOwner && member.userId !== currentUserId && (
                  <Button
                    variant="ghost"
                    size="sm"
                    className="text-destructive hover:text-destructive"
                    onClick={() => setMemberToRemove(member.userId)}
                  >
                    Remove
                  </Button>
                )}
              </div>
            ))}
          </div>
        )}
      </div>

      <ConfirmDialog
        open={!!memberToRemove}
        onOpenChange={(open) => !open && setMemberToRemove(null)}
        title="Remove member"
        description="Are you sure you want to remove this member? They will lose access immediately."
        confirmLabel="Remove"
        confirmVariant="destructive"
        onConfirm={handleRemove}
        isLoading={remove.isPending}
      />
    </AppShell>
  );
}
