import { useState } from "react";
import { useNavigate, useParams, Link } from "react-router-dom";
import { useForm } from "react-hook-form";
import { toast } from "sonner";
import { ArrowLeft } from "lucide-react";
import { useSubmitJob } from "@/api/jobs";
import { UploadDropzone } from "@/components/jobs/UploadDropzone";
import { AppShell } from "@/components/layout/AppShell";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import type { AxiosError } from "axios";

interface FormValues {
  title: string;
}

export function UploadJobPage() {
  const { orgId } = useParams<{ orgId: string }>();
  const navigate = useNavigate();
  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [fileError, setFileError] = useState<string | undefined>();
  const [uploadProgress, setUploadProgress] = useState<number | null>(null);
  const submitJob = useSubmitJob(orgId!);

  const { register, handleSubmit } = useForm<FormValues>();

  async function onSubmit(values: FormValues) {
    if (!selectedFile) {
      setFileError("Please select a file to upload.");
      return;
    }

    setFileError(undefined);
    const formData = new FormData();
    formData.append("Artifact", selectedFile, selectedFile.name);
    if (values.title.trim()) formData.append("Title", values.title.trim());

    setUploadProgress(0);

    try {
      const job = await submitJob.mutateAsync(formData);
      toast.success("Job submitted");
      navigate(`/orgs/${orgId!}/jobs/${job.id}`);
    } catch (err) {
      const axiosErr = err as AxiosError<{ code?: string }>;
      const status = axiosErr.response?.status;
      if (status === 413) {
        setFileError("File is too large (max 10 MB).");
      } else if (status === 415) {
        setFileError("File type not supported.");
      } else if (status === 429) {
        toast.error("Too many submissions — try again shortly.");
      } else {
        toast.error("Failed to submit job. Please try again.");
      }
    } finally {
      setUploadProgress(null);
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
          <h1 className="text-2xl font-bold">Upload architecture</h1>
        </div>

        <form onSubmit={handleSubmit(onSubmit)} className="space-y-6">
          <UploadDropzone
            onFileSelected={(f) => {
              setSelectedFile(f);
              setFileError(undefined);
            }}
            selectedFile={selectedFile}
            onFileClear={() => setSelectedFile(null)}
            error={fileError}
          />

          <div className="space-y-1.5">
            <Label htmlFor="title">Title (optional)</Label>
            <Input
              id="title"
              {...register("title")}
              placeholder="e.g. Payment Service v2"
              maxLength={255}
            />
          </div>

          {uploadProgress !== null && (
            <div className="space-y-1">
              <div className="h-2 w-full rounded-full bg-muted overflow-hidden">
                <div
                  className="h-full bg-primary transition-all duration-300"
                  style={{ width: `${uploadProgress}%` }}
                />
              </div>
              <p className="text-xs text-muted-foreground">Uploading…</p>
            </div>
          )}

          <Button
            type="submit"
            className="w-full"
            disabled={submitJob.isPending || uploadProgress !== null}
          >
            {submitJob.isPending ? "Submitting…" : "Submit for analysis"}
          </Button>
        </form>
      </div>
    </AppShell>
  );
}
