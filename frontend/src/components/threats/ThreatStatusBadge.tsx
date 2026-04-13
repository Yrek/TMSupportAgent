import { Badge } from "@/components/ui/badge";
import type { ThreatStatus } from "@/lib/constants";

const STATUS_CONFIG: Record<ThreatStatus, { label: string; variant: "secondary" | "info" | "success" | "destructive" }> = {
  Open:      { label: "Open",      variant: "secondary" },
  Accepted:  { label: "Accepted",  variant: "info" },
  Mitigated: { label: "Mitigated", variant: "success" },
  Rejected:  { label: "Rejected",  variant: "destructive" },
};

interface ThreatStatusBadgeProps {
  status: ThreatStatus;
  className?: string;
}

export function ThreatStatusBadge({ status, className }: ThreatStatusBadgeProps) {
  const cfg = STATUS_CONFIG[status] ?? { label: status, variant: "secondary" as const };
  return <Badge variant={cfg.variant} className={className}>{cfg.label}</Badge>;
}
