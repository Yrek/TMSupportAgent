import { useState, useEffect, useRef, useMemo } from "react";
import { useParams, Link } from "react-router-dom";
import { usePageTitle } from "@/hooks/usePageTitle";
import { toast } from "sonner";
import { PlusCircle } from "lucide-react";
import { useJobs, useDeleteJob, type JobSummary } from "@/api/jobs";
import { useOrgStats } from "@/api/orgs";
import { JobCard } from "@/components/jobs/JobCard";
import { ConfirmDialog } from "@/components/common/ConfirmDialog";
import { AppShell } from "@/components/layout/AppShell";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import type { JobStatus } from "@/lib/constants";
import { requiredParam } from "@/lib/requiredParam";

const STATUS_FILTERS: Array<{ label: string; value: JobStatus | "all" }> = [
  { label: "All", value: "all" },
  { label: "In Progress", value: "Analyzing" },
  { label: "Awaiting Review", value: "AwaitingReview" },
  { label: "Complete", value: "Complete" },
  { label: "Failed", value: "Failed" },
];

export function DashboardPage() {
  const params = useParams<{ orgId: string }>();
  const orgId = requiredParam(params.orgId, "orgId");
  const [statusFilter, setStatusFilter] = useState<JobStatus | "all">("all");
  const [jobToDelete, setJobToDelete] = useState<JobSummary | null>(null);
  const prevStatuses = useRef<Map<string, JobStatus>>(new Map());
  usePageTitle("Jobs");

  const { data: statsData } = useOrgStats(orgId);

  const { data, isLoading } = useJobs(orgId, {
    status: statusFilter === "all" ? undefined : statusFilter,
    pageSize: 20,
  });

  const deleteJob = useDeleteJob(orgId);
  const jobs = useMemo(() => data?.data ?? [], [data?.data]);

  // Auto-navigate and toast on status transitions
  useEffect(() => {
    jobs.forEach((job) => {
      const prev = prevStatuses.current.get(job.id);
      if (prev && prev !== job.status) {
        if (job.status === "AwaitingReview") {
          toast.info(`"${job.title ?? "Job"}" is ready for review`);
        } else if (job.status === "Complete" || job.status === "Partial") {
          toast.success(`"${job.title ?? "Job"}" analysis complete`);
        } else if (job.status === "Failed") {
          toast.error(`"${job.title ?? "Job"}" failed`);
        }
      }
      prevStatuses.current.set(job.id, job.status);
    });
  }, [jobs]);

  async function confirmDelete() {
    if (!jobToDelete) return;
    try {
      await deleteJob.mutateAsync(jobToDelete.id);
      toast.success("Job deleted");
    } catch {
      toast.error("Failed to delete job");
    } finally {
      setJobToDelete(null);
    }
  }

  return (
    <AppShell>
      <div className="mx-auto max-w-4xl space-y-6 p-6">
        {statsData && (
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            {[
              { label: "Total", value: statsData.totalJobs },
              { label: "Complete", value: (statsData.byStatus["Complete"] ?? 0) + (statsData.byStatus["Partial"] ?? 0) },
              { label: "In progress", value: Object.entries(statsData.byStatus).filter(([k]) => !["Complete","Partial","Failed"].includes(k)).reduce((a, [,v]) => a + v, 0) },
              { label: "Members", value: statsData.activeMembers },
            ].map((s) => (
              <div key={s.label} className="rounded-lg border p-3 text-center">
                <p className="text-xl font-bold">{s.value}</p>
                <p className="text-xs text-muted-foreground">{s.label}</p>
              </div>
            ))}
          </div>
        )}

        <div className="flex items-center justify-between">
          <h1 className="text-2xl font-bold">Jobs</h1>
          <Button asChild>
            <Link to={`/orgs/${orgId}/jobs/new`}>
              <PlusCircle className="mr-2 h-4 w-4" />
              New analysis
            </Link>
          </Button>
        </div>

        {/* Status filter tabs */}
        <div className="flex gap-1 overflow-x-auto border-b pb-0">
          {STATUS_FILTERS.map((f) => (
            <button
              key={f.value}
              onClick={() => setStatusFilter(f.value)}
              className={`shrink-0 border-b-2 px-4 pb-2 text-sm transition-colors ${
                statusFilter === f.value
                  ? "border-primary text-primary font-medium"
                  : "border-transparent text-muted-foreground hover:text-foreground"
              }`}
            >
              {f.label}
            </button>
          ))}
        </div>

        {isLoading ? (
          <div className="space-y-3">
            {[1, 2, 3].map((i) => (
              <Skeleton key={i} className="h-20 w-full" />
            ))}
          </div>
        ) : !jobs.length ? (
          <div className="flex flex-col items-center justify-center gap-4 rounded-lg border border-dashed p-12 text-center">
            <PlusCircle className="h-12 w-12 text-muted-foreground" />
            <div>
              <h3 className="font-semibold">No jobs yet</h3>
              <p className="mt-1 text-sm text-muted-foreground">
                Start by uploading an architecture diagram or drawing one manually.
              </p>
            </div>
            <Button asChild>
              <Link to={`/orgs/${orgId}/jobs/new`}>
                <PlusCircle className="mr-2 h-4 w-4" />
                New analysis
              </Link>
            </Button>
          </div>
        ) : (
          <div className="space-y-3">
            {jobs.map((job) => (
              <JobCard
                key={job.id}
                job={job}
                orgId={orgId}
                onDelete={setJobToDelete}
              />
            ))}
          </div>
        )}

        {data?.pagination?.hasMore && (
          <p className="text-center text-sm text-muted-foreground">
            More jobs available — pagination coming soon.
          </p>
        )}
      </div>

      <ConfirmDialog
        open={!!jobToDelete}
        onOpenChange={(open) => !open && setJobToDelete(null)}
        title="Delete job"
        description={`Delete "${jobToDelete?.title ?? "this job"}"? This action cannot be undone.`}
        confirmLabel="Delete"
        confirmVariant="destructive"
        onConfirm={confirmDelete}
        isLoading={deleteJob.isPending}
      />
    </AppShell>
  );
}
