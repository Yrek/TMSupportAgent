import { useNavigate } from "react-router-dom";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { toast } from "sonner";
import { useCreateOrg } from "@/api/orgs";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { ShieldCheck } from "lucide-react";
import type { AxiosError } from "axios";

const schema = z.object({
  name: z.string().min(2, "Name must be at least 2 characters").max(100),
  slug: z
    .string()
    .min(2, "Slug must be at least 2 characters")
    .max(50)
    .regex(
      /^[a-z0-9][a-z0-9-]*[a-z0-9]$/,
      "Slug must be lowercase alphanumeric with hyphens, no leading/trailing hyphens",
    ),
});

type FormValues = z.infer<typeof schema>;

export function CreateOrgPage() {
  const navigate = useNavigate();
  const createOrg = useCreateOrg();

  const {
    register,
    handleSubmit,
    setError,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({ resolver: zodResolver(schema) });

  async function onSubmit(values: FormValues) {
    try {
      const org = await createOrg.mutateAsync(values);
      toast.success("Organisation created");
      navigate(`/orgs/${org.id}/jobs`);
    } catch (err) {
      const axiosErr = err as AxiosError<{ code?: string; message?: string }>;
      if (axiosErr.response?.status === 409) {
        setError("slug", { message: "Slug already taken — choose a different one" });
      } else {
        toast.error("Failed to create organisation");
      }
    }
  }

  return (
    <div className="flex min-h-screen flex-col items-center justify-center gap-8 p-8">
      <div className="w-full max-w-md space-y-6">
        <div className="flex flex-col items-center gap-2 text-center">
          <ShieldCheck className="h-10 w-10 text-primary" />
          <h1 className="text-2xl font-bold">Create organisation</h1>
          <p className="text-sm text-muted-foreground">
            Give your organisation a name and a unique URL slug.
          </p>
        </div>

        <form onSubmit={handleSubmit(onSubmit)} className="space-y-4">
          <div className="space-y-1.5">
            <Label htmlFor="name">Name</Label>
            <Input id="name" {...register("name")} placeholder="Acme Corp" />
            {errors.name && (
              <p className="text-sm text-destructive">{errors.name.message}</p>
            )}
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="slug">URL slug</Label>
            <Input id="slug" {...register("slug")} placeholder="acme-corp" />
            <p className="text-xs text-muted-foreground">
              Lowercase letters, numbers, and hyphens only. Cannot start or end with a hyphen.
            </p>
            {errors.slug && (
              <p className="text-sm text-destructive">{errors.slug.message}</p>
            )}
          </div>

          <Button type="submit" className="w-full" disabled={isSubmitting}>
            {isSubmitting ? "Creating…" : "Create organisation"}
          </Button>
        </form>
      </div>
    </div>
  );
}
