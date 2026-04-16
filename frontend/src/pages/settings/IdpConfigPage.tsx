import { useState } from "react";
import { useParams } from "react-router-dom";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { toast } from "sonner";
import { PlusCircle, X } from "lucide-react";
import { useIdpConfig, useUpsertIdpConfig, useDeleteIdpConfig } from "@/api/idp";
import { AppShell } from "@/components/layout/AppShell";
import { ConfirmDialog } from "@/components/common/ConfirmDialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Separator } from "@/components/ui/separator";
import { Badge } from "@/components/ui/badge";
import { requiredParam } from "@/lib/requiredParam";

const schema = z.object({
  providerType: z.string().min(1),
  workosConnectionId: z.string().min(1, "WorkOS Connection ID is required"),
});
type FormValues = z.infer<typeof schema>;

const PROVIDER_TYPES = ["okta", "google", "azure_ad", "generic_saml", "generic_oidc"];

export function IdpConfigPage() {
  const params = useParams<{ orgId: string }>();
  const orgId = requiredParam(params.orgId, "orgId");
  const { data: config } = useIdpConfig(orgId);
  const upsert = useUpsertIdpConfig(orgId);
  const deleteConfig = useDeleteIdpConfig(orgId);

  const [domainHints, setDomainHints] = useState<string[]>(config?.domainHints ?? []);
  const [newHint, setNewHint] = useState("");
  const [showDeleteDialog, setShowDeleteDialog] = useState(false);

  const { register, handleSubmit, setValue, watch, formState: { errors, isSubmitting } } = useForm<FormValues>({
    resolver: zodResolver(schema),
    ...(config ? { values: { providerType: config.providerType, workosConnectionId: "" } } : {}),
  });

  const providerType = watch("providerType") ?? "generic_oidc";

  async function onSubmit(values: FormValues) {
    try {
      await upsert.mutateAsync({ ...values, domainHints });
      toast.success("IDP configuration saved");
    } catch {
      toast.error("Failed to save IDP configuration");
    }
  }

  async function handleDelete() {
    try {
      await deleteConfig.mutateAsync();
      toast.success("IDP configuration deleted");
    } catch {
      toast.error("Failed to delete IDP configuration");
    } finally {
      setShowDeleteDialog(false);
    }
  }

  function addHint() {
    const hint = newHint.trim().toLowerCase();
    if (hint && !domainHints.includes(hint)) {
      setDomainHints((prev) => [...prev, hint]);
    }
    setNewHint("");
  }

  return (
    <AppShell>
      <div className="mx-auto max-w-2xl space-y-8 p-6">
        <div>
          <h1 className="text-2xl font-bold">Enterprise SSO</h1>
          <p className="mt-1 text-sm text-muted-foreground">
            Configure a SAML or OIDC identity provider for your organisation. The WorkOS connection must be created in the WorkOS dashboard first.
          </p>
        </div>

        {config && (
          <div className="rounded-lg border bg-muted/30 p-3 text-sm space-y-1">
            <p><span className="font-medium">Provider:</span> {config.providerType}</p>
            <p><span className="font-medium">Domain hints:</span> {config.domainHints.join(", ") || "None"}</p>
          </div>
        )}

        <form onSubmit={handleSubmit(onSubmit)} className="space-y-5">
          <div className="space-y-1.5">
            <Label>Provider type</Label>
            <Select value={providerType} onValueChange={(v) => setValue("providerType", v)}>
              <SelectTrigger>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {PROVIDER_TYPES.map((t) => (
                  <SelectItem key={t} value={t}>{t}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="connId">WorkOS Connection ID</Label>
            <Input id="connId" {...register("workosConnectionId")} placeholder="conn_..." />
            {errors.workosConnectionId && (
              <p className="text-sm text-destructive">{errors.workosConnectionId.message}</p>
            )}
          </div>

          <div className="space-y-1.5">
            <Label>Domain hints</Label>
            <div className="flex gap-2">
              <Input
                value={newHint}
                onChange={(e) => setNewHint(e.target.value)}
                placeholder="example.com"
                onKeyDown={(e) => { if (e.key === "Enter") { e.preventDefault(); addHint(); } }}
              />
              <Button type="button" variant="outline" onClick={addHint}>
                <PlusCircle className="h-4 w-4" />
              </Button>
            </div>
            {domainHints.length > 0 && (
              <div className="flex flex-wrap gap-1 mt-2">
                {domainHints.map((h) => (
                  <Badge key={h} variant="secondary" className="gap-1">
                    {h}
                    <button onClick={() => setDomainHints((prev) => prev.filter((d) => d !== h))} aria-label={`Remove ${h}`}>
                      <X className="h-3 w-3" />
                    </button>
                  </Badge>
                ))}
              </div>
            )}
          </div>

          <Button type="submit" disabled={isSubmitting}>
            {isSubmitting ? "Saving…" : config ? "Update configuration" : "Save configuration"}
          </Button>
        </form>

        {config && (
          <>
            <Separator />
            <div className="rounded-lg border border-destructive/30 p-4 space-y-3">
              <h2 className="font-semibold text-destructive">Remove SSO</h2>
              <p className="text-sm text-muted-foreground">
                Removing the IDP configuration will disable SSO for this organisation.
              </p>
              <Button variant="destructive" size="sm" onClick={() => setShowDeleteDialog(true)}>
                Delete IDP configuration
              </Button>
            </div>
          </>
        )}
      </div>

      <ConfirmDialog
        open={showDeleteDialog}
        onOpenChange={setShowDeleteDialog}
        title="Delete IDP configuration"
        description="This will disable enterprise SSO for your organisation. Members will need to use other sign-in methods."
        confirmLabel="Delete"
        confirmVariant="destructive"
        onConfirm={handleDelete}
        isLoading={deleteConfig.isPending}
      />
    </AppShell>
  );
}
