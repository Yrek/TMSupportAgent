import { Badge } from "@/components/ui/badge";

type FindingType = "Confirmed" | "Conditional" | "UserAdded";

const FINDING_CONFIG: Record<FindingType, { label: string; variant: "success" | "warning" | "purple" }> = {
  Confirmed:  { label: "Confirmed",  variant: "success" },
  Conditional: { label: "Conditional", variant: "warning" },
  UserAdded:  { label: "User Added", variant: "purple" },
};

interface FindingTypeBadgeProps {
  findingType: FindingType;
  className?: string;
}

export function FindingTypeBadge({ findingType, className }: FindingTypeBadgeProps) {
  const cfg = FINDING_CONFIG[findingType] ?? { label: findingType, variant: "secondary" as const };
  return <Badge variant={cfg.variant} className={className}>{cfg.label}</Badge>;
}
