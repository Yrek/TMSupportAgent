import { Link } from "react-router-dom";
import { formatDistanceToNow } from "date-fns";
import { Cpu, FileText, Trash2 } from "lucide-react";
import type { JobSummary } from "@/api/jobs";
import { JobStatusBadge } from "./JobStatusBadge";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";

interface JobCardProps {
  job: JobSummary;
  orgId: string;
  onDelete?: (job: JobSummary) => void;
}

function getJobCta(job: JobSummary, orgId: string): { label: string; to: string } | null {
  if (job.status === "AwaitingReview")
    return { label: "Review →", to: `/orgs/${orgId}/jobs/${job.id}/review` };
  if (job.status === "Complete" || job.status === "Partial")
    return { label: "View →", to: `/orgs/${orgId}/jobs/${job.id}/analysis` };
  if (job.status === "Failed")
    return { label: "Details", to: `/orgs/${orgId}/jobs/${job.id}` };
  return { label: "Progress →", to: `/orgs/${orgId}/jobs/${job.id}` };
}

export function JobCard({ job, orgId, onDelete }: JobCardProps) {
  const cta = getJobCta(job, orgId);

  return (
    <div className="flex items-center gap-4 rounded-lg border bg-card p-4 transition-colors hover:bg-muted/30">
      <div className="flex-1 min-w-0 space-y-1">
        <div className="flex items-center gap-2 flex-wrap">
          <span className="font-medium truncate">{job.title ?? "Untitled"}</span>
          <JobStatusBadge status={job.status} />
          {job.isManual ? (
            <Badge variant="outline" className="gap-1 text-xs">
              <Cpu className="h-3 w-3" />
              Manual
            </Badge>
          ) : job.artifactType ? (
            <Badge variant="outline" className="gap-1 text-xs">
              <FileText className="h-3 w-3" />
              {job.artifactType}
            </Badge>
          ) : null}
        </div>
        <p className="text-xs text-muted-foreground">
          Created {formatDistanceToNow(new Date(job.createdAt), { addSuffix: true })}
          {job.completedAt &&
            ` · Completed ${formatDistanceToNow(new Date(job.completedAt), { addSuffix: true })}`}
        </p>
      </div>

      <div className="flex items-center gap-2 shrink-0">
        {cta && (
          <Button asChild size="sm" variant="outline">
            <Link to={cta.to}>{cta.label}</Link>
          </Button>
        )}

        {onDelete && (
          <Button
            size="icon"
            variant="ghost"
            className={cn("h-8 w-8")}
            onClick={() => onDelete(job)}
            aria-label="Delete job"
          >
            <Trash2 className="h-4 w-4 text-muted-foreground" />
          </Button>
        )}
      </div>
    </div>
  );
}
