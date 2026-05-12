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
import { Textarea } from "@/components/ui/textarea";
import { Tabs, TabsList, TabsTrigger, TabsContent } from "@/components/ui/tabs";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import type { AxiosError } from "axios";
import { requiredParam } from "@/lib/requiredParam";

interface FormValues {
  title: string;
  applicationDescription: string;
  architectureDescription: string;
}

type DiagramFormat = "mermaid" | "plantuml" | "drawio" | "text";

const FORMAT_META: Record<DiagramFormat, { label: string; ext: string; placeholder: string }> = {
  mermaid: {
    label: "Mermaid",
    ext: ".mmd",
    placeholder: `graph TD
  User -->|HTTPS| LoadBalancer
  LoadBalancer --> API
  API --> Database`,
  },
  plantuml: {
    label: "PlantUML",
    ext: ".puml",
    placeholder: `@startuml
actor User
User -> LoadBalancer : HTTPS
LoadBalancer -> API
API -> Database
@enduml`,
  },
  drawio: {
    label: "Draw.io XML",
    ext: ".xml",
    placeholder: `<mxfile><diagram>...</diagram></mxfile>`,
  },
  text: {
    label: "Plain text",
    ext: ".txt",
    placeholder: "Describe your architecture as free-form text.",
  },
};

function codeToFile(code: string, format: DiagramFormat): File {
  const { ext } = FORMAT_META[format];
  return new File([code], `diagram${ext}`, { type: "text/plain" });
}

export function UploadJobPage() {
  const params = useParams<{ orgId: string }>();
  const orgId = requiredParam(params.orgId, "orgId");
  const navigate = useNavigate();

  const [selectedFile, setSelectedFile] = useState<File | null>(null);
  const [fileError, setFileError] = useState<string | undefined>();
  const [uploadProgress, setUploadProgress] = useState<number | null>(null);

  const [inputMode, setInputMode] = useState<"file" | "code">("file");
  const [diagramCode, setDiagramCode] = useState("");
  const [diagramFormat, setDiagramFormat] = useState<DiagramFormat>("mermaid");

  const submitJob = useSubmitJob(orgId);
  const { register, handleSubmit } = useForm<FormValues>();

  async function onSubmit(values: FormValues) {
    let artifact: File | null = null;

    if (inputMode === "file") {
      if (!selectedFile) {
        setFileError("Please select a file to upload.");
        return;
      }
      artifact = selectedFile;
    } else {
      const trimmed = diagramCode.trim();
      if (!trimmed) {
        setFileError("Please paste your diagram code.");
        return;
      }
      artifact = codeToFile(trimmed, diagramFormat);
    }

    setFileError(undefined);
    const formData = new FormData();
    formData.append("Artifact", artifact, artifact.name);
    if (values.title.trim()) formData.append("Title", values.title.trim());
    if (values.applicationDescription.trim()) {
      formData.append("ApplicationDescription", values.applicationDescription.trim());
    }
    if (values.architectureDescription.trim()) {
      formData.append("ArchitectureDescription", values.architectureDescription.trim());
    }

    setUploadProgress(0);

    try {
      const job = await submitJob.mutateAsync(formData);
      toast.success("Job submitted");
      navigate(`/orgs/${orgId}/jobs/${job.id}`);
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
            to={`/orgs/${orgId}/jobs/new`}
            className="text-muted-foreground hover:text-foreground transition-colors"
            aria-label="Back"
          >
            <ArrowLeft className="h-5 w-5" />
          </Link>
          <h1 className="text-2xl font-bold">Upload architecture</h1>
        </div>

        <form onSubmit={handleSubmit(onSubmit)} className="space-y-6">
          <div className="space-y-1.5">
            <Label htmlFor="title">Title (optional)</Label>
            <Input
              id="title"
              {...register("title")}
              placeholder="e.g. Payment Service v2"
              maxLength={255}
            />
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="applicationDescription">Application Description (optional)</Label>
            <Textarea
              id="applicationDescription"
              {...register("applicationDescription")}
              placeholder="What does this application do? Main users, business purpose, and critical flows."
              rows={3}
              maxLength={2000}
            />
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="architectureDescription">Architecture Description (optional)</Label>
            <Textarea
              id="architectureDescription"
              {...register("architectureDescription")}
              placeholder="Any context not obvious from the diagram (trust boundaries, assumptions, external dependencies)."
              rows={4}
              maxLength={20000}
            />
          </div>

          <div className="space-y-2">
            <Label>Architecture diagram</Label>
            <Tabs
              value={inputMode}
              onValueChange={(v) => {
                setInputMode(v as "file" | "code");
                setFileError(undefined);
              }}
            >
              <TabsList>
                <TabsTrigger value="file">Upload file</TabsTrigger>
                <TabsTrigger value="code">Paste code</TabsTrigger>
              </TabsList>

              <TabsContent value="file">
                <UploadDropzone
                  onFileSelected={(f) => {
                    setSelectedFile(f);
                    setFileError(undefined);
                  }}
                  selectedFile={selectedFile}
                  onFileClear={() => setSelectedFile(null)}
                  error={fileError}
                />
              </TabsContent>

              <TabsContent value="code" className="space-y-3">
                <div className="flex items-center gap-3">
                  <Label htmlFor="diagram-format" className="shrink-0 text-sm">
                    Format
                  </Label>
                  <Select
                    value={diagramFormat}
                    onValueChange={(v) => setDiagramFormat(v as DiagramFormat)}
                  >
                    <SelectTrigger id="diagram-format" className="w-40">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {(Object.keys(FORMAT_META) as DiagramFormat[]).map((fmt) => (
                        <SelectItem key={fmt} value={fmt}>
                          {FORMAT_META[fmt].label}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>

                <Textarea
                  value={diagramCode}
                  onChange={(e) => {
                    setDiagramCode(e.target.value);
                    setFileError(undefined);
                  }}
                  placeholder={FORMAT_META[diagramFormat].placeholder}
                  rows={10}
                  className="font-mono text-sm"
                  maxLength={500_000}
                  aria-label="Diagram code"
                />

                {fileError && (
                  <p className="text-sm text-destructive" role="alert">
                    {fileError}
                  </p>
                )}
              </TabsContent>
            </Tabs>
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
