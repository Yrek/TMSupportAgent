import { Badge } from "@/components/ui/badge";
import type { JobStatus } from "@/lib/constants";

interface JobStatusBadgeProps {
  status: JobStatus;
  className?: string;
}

const STATUS_CONFIG: Record<
  JobStatus,
  { label: string; variant: "default" | "secondary" | "destructive" | "outline" | "success" | "warning" | "info" | "orange" }
> = {
  Pending:        { label: "Pending",          variant: "secondary" },
  Parsing:        { label: "Parsing",          variant: "info" },
  Normalizing:    { label: "Normalizing",      variant: "info" },
  AwaitingReview: { label: "Awaiting Review",  variant: "warning" },
  Classifying:    { label: "Classifying",      variant: "info" },
  Analyzing:      { label: "Analyzing",        variant: "info" },
  Synthesizing:   { label: "Synthesizing",     variant: "info" },
  Complete:       { label: "Complete",         variant: "success" },
  Failed:         { label: "Failed",           variant: "destructive" },
  Partial:        { label: "Partial",          variant: "orange" },
};

export function JobStatusBadge({ status, className }: JobStatusBadgeProps) {
  const config = STATUS_CONFIG[status] ?? { label: status, variant: "secondary" as const };
  return (
    <Badge variant={config.variant} className={className}>
      {config.label}
    </Badge>
  );
}
