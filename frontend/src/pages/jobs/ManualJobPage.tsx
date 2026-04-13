import { useNavigate, useParams, Link } from "react-router-dom";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { toast } from "sonner";
import { ArrowLeft } from "lucide-react";
import { useCreateManualJob } from "@/api/jobs";
import { AppShell } from "@/components/layout/AppShell";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Label } from "@/components/ui/label";

const schema = z.object({
  title: z.string().max(255).optional(),
  systemPurpose: z.string().max(2000).optional(),
});

type FormValues = z.infer<typeof schema>;

export function ManualJobPage() {
  const { orgId } = useParams<{ orgId: string }>();
  const navigate = useNavigate();
  const createManualJob = useCreateManualJob(orgId!);

  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({ resolver: zodResolver(schema) });

  async function onSubmit(values: FormValues) {
    try {
      const job = await createManualJob.mutateAsync({
        title: values.title || undefined,
        systemPurpose: values.systemPurpose || undefined,
      });
      toast.success("Manual job created — add your architecture elements below");
      navigate(`/orgs/${orgId!}/jobs/${job.id}/review`);
    } catch {
      toast.error("Failed to create job. Please try again.");
    }
  }

  return (
    <AppShell>
      <div className="mx-auto max-w-2xl space-y-6 p-6">
        <div className="flex items-center gap-3">
          <Link
            to={`/orgs/${orgId!}/jobs/new`}
            className="text-muted-foreground hover:text-foreground transition-colors"
            aria-label="Back"
          >
            <ArrowLeft className="h-5 w-5" />
          </Link>
          <h1 className="text-2xl font-bold">Draw architecture manually</h1>
        </div>

        <p className="text-muted-foreground text-sm">
          Start with an optional title and system description, then add elements on the canvas.
        </p>

        <form onSubmit={handleSubmit(onSubmit)} className="space-y-5">
          <div className="space-y-1.5">
            <Label htmlFor="title">Title (optional)</Label>
            <Input
              id="title"
              {...register("title")}
              placeholder="e.g. Checkout Service"
              maxLength={255}
            />
            {errors.title && (
              <p className="text-sm text-destructive">{errors.title.message}</p>
            )}
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="systemPurpose">System purpose (optional)</Label>
            <Textarea
              id="systemPurpose"
              {...register("systemPurpose")}
              placeholder="Describe what this system does and who uses it…"
              rows={4}
              maxLength={2000}
            />
            {errors.systemPurpose && (
              <p className="text-sm text-destructive">{errors.systemPurpose.message}</p>
            )}
          </div>

          <Button type="submit" className="w-full" disabled={isSubmitting}>
            {isSubmitting ? "Creating…" : "Create and start drawing"}
          </Button>
        </form>
      </div>
    </AppShell>
  );
}
