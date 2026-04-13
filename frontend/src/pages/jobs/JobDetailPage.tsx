import { useEffect, useRef } from "react";
import { useParams, Link, useNavigate } from "react-router-dom";
import { usePageTitle } from "@/hooks/usePageTitle";
import { toast } from "sonner";
import { Check, Circle, ArrowRight, AlertCircle, Cpu, FileText, ArrowLeft } from "lucide-react";
import { useJob } from "@/api/jobs";
import { JobStatusBadge } from "@/components/jobs/JobStatusBadge";
import { AppShell } from "@/components/layout/AppShell";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import type { JobStatus } from "@/lib/constants";
import { cn } from "@/lib/utils";

const PIPELINE_STAGES: Array<{ status: JobStatus; label: string }> = [
  { status: "Pending", label: "Pending" },
  { status: "Parsing", label: "Parsing" },
  { status: "Normalizing", label: "Normalizing" },
  { status: "AwaitingReview", label: "Awaiting Review" },
  { status: "Classifying", label: "Classifying" },
  { status: "Analyzing", label: "Analyzing" },
  { status: "Synthesizing", label: "Synthesizing" },
  { status: "Complete", label: "Complete" },
];

const STATUS_ORDER: Partial<Record<JobStatus, number>> = {
  Pending: 0,
  Parsing: 1,
  Normalizing: 2,
  AwaitingReview: 3,
  Classifying: 4,
  Analyzing: 5,
  Synthesizing: 6,
  Complete: 7,
  Partial: 7,
  Failed: -1,
};

function getStageState(
  stageStatus: JobStatus,
  currentStatus: JobStatus,
): "complete" | "current" | "pending" | "failed" {
  if (currentStatus === "Failed") {
    const stageIdx = STATUS_ORDER[stageStatus] ?? 0;
    const currentIdx = STATUS_ORDER["AwaitingReview"] ?? 3;
    if (stageIdx < currentIdx) return "complete";
    return "pending";
  }
  const stageIdx = STATUS_ORDER[stageStatus] ?? 0;
  const currentIdx = STATUS_ORDER[currentStatus] ?? 0;
  if (stageIdx < currentIdx) return "complete";
  if (stageIdx === currentIdx) return "current";
  return "pending";
}

export function JobDetailPage() {
  const { orgId, jobId } = useParams<{ orgId: string; jobId: string }>();
  const navigate = useNavigate();
  const { data: job, isLoading } = useJob(orgId!, jobId!);
  const prevStatus = useRef<JobStatus | null>(null);
  usePageTitle(job?.title ?? "Job");

  // Auto-navigate on terminal transitions
  useEffect(() => {
    if (!job) return;
    const prev = prevStatus.current;
    if (prev && prev !== job.status) {
      if (job.status === "AwaitingReview") {
        toast.info("Architecture ready for review");
        navigate(`/orgs/${orgId!}/jobs/${jobId!}/review`, { replace: true });
      } else if (job.status === "Complete" || job.status === "Partial") {
        toast.success("Analysis complete");
        navigate(`/orgs/${orgId!}/jobs/${jobId!}/analysis`, { replace: true });
      } else if (job.status === "Failed") {
        toast.error(`Job failed${job.errorCode ? `: ${job.errorCode}` : ""}`);
      }
    }
    prevStatus.current = job.status;
  }, [job, orgId, jobId, navigate]);

  if (isLoading) {
    return (
      <AppShell>
        <div className="mx-auto max-w-2xl space-y-6 p-6">
          <Skeleton className="h-8 w-64" />
          <Skeleton className="h-4 w-32" />
          <div className="space-y-4">
            {PIPELINE_STAGES.map((s) => (
              <Skeleton key={s.status} className="h-12 w-full" />
            ))}
          </div>
        </div>
      </AppShell>
    );
  }

  if (!job) return null;

  return (
    <AppShell>
      <div className="mx-auto max-w-2xl space-y-6 p-6">
        <div className="flex items-center gap-3">
          <Link
            to={`/orgs/${orgId!}/jobs`}
            className="text-muted-foreground hover:text-foreground transition-colors"
            aria-label="Back to jobs"
          >
            <ArrowLeft className="h-5 w-5" />
          </Link>
          <div className="flex-1 min-w-0">
            <h1 className="truncate text-2xl font-bold">{job.title ?? "Untitled job"}</h1>
          </div>
          <JobStatusBadge status={job.status} />
        </div>

        {/* Artifact type */}
        <div className="flex items-center gap-2 text-sm text-muted-foreground">
          {job.isManual ? (
            <>
              <Cpu className="h-4 w-4" />
              <span>Manual job (no artifact)</span>
            </>
          ) : job.artifactType ? (
            <>
              <FileText className="h-4 w-4" />
              <span>{job.artifactType}</span>
            </>
          ) : null}
        </div>

        {/* Pipeline stepper */}
        <div className="space-y-0">
          {PIPELINE_STAGES.map((stage, idx) => {
            const state = getStageState(stage.status, job.status);
            return (
              <div key={stage.status} className="flex gap-4">
                {/* Icon column */}
                <div className="flex flex-col items-center">
                  <div
                    className={cn(
                      "flex h-8 w-8 items-center justify-center rounded-full border-2 transition-colors",
                      state === "complete" && "border-green-500 bg-green-500 text-white",
                      state === "current" && "border-primary bg-primary text-primary-foreground",
                      state === "pending" && "border-muted bg-background text-muted-foreground",
                    )}
                    aria-label={`${stage.label}: ${state}`}
                  >
                    {state === "complete" ? (
                      <Check className="h-4 w-4" />
                    ) : state === "current" && job.status === "Failed" ? (
                      <AlertCircle className="h-4 w-4" />
                    ) : (
                      <Circle className="h-4 w-4" />
                    )}
                  </div>
                  {idx < PIPELINE_STAGES.length - 1 && (
                    <div
                      className={cn(
                        "w-0.5 flex-1 my-0.5",
                        state === "complete" ? "bg-green-500" : "bg-muted",
                      )}
                      style={{ minHeight: "24px" }}
                    />
                  )}
                </div>

                {/* Label */}
                <div
                  className={cn(
                    "pb-6 pt-1 text-sm font-medium",
                    state === "current" && "text-foreground",
                    state === "complete" && "text-muted-foreground",
                    state === "pending" && "text-muted-foreground",
                  )}
                >
                  {stage.label}
                  {state === "current" && job.status !== "Failed" && (
                    <span className="ml-2 text-xs text-primary animate-pulse">In progress…</span>
                  )}
                </div>
              </div>
            );
          })}
        </div>

        {/* CTAs */}
        {job.status === "AwaitingReview" && (
          <Button asChild className="w-full">
            <Link to={`/orgs/${orgId!}/jobs/${jobId!}/review`}>
              Review architecture
              <ArrowRight className="ml-2 h-4 w-4" />
            </Link>
          </Button>
        )}

        {(job.status === "Complete" || job.status === "Partial") && (
          <Button asChild className="w-full">
            <Link to={`/orgs/${orgId!}/jobs/${jobId!}/analysis`}>
              View threat model
              <ArrowRight className="ml-2 h-4 w-4" />
            </Link>
          </Button>
        )}

        {job.status === "Failed" && (
          <div className="rounded-lg border border-destructive/30 bg-destructive/5 p-4">
            <div className="flex items-center gap-2 text-destructive font-medium">
              <AlertCircle className="h-4 w-4" />
              Job failed
            </div>
            {job.errorCode && (
              <p className="mt-1 text-sm text-muted-foreground">Error code: {job.errorCode}</p>
            )}
          </div>
        )}
      </div>
    </AppShell>
  );
}
