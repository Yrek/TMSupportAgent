import type { Threat } from "@/api/threats";
import { FindingTypeBadge } from "./FindingTypeBadge";
import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";

interface ThreatCardProps {
  threat: Threat;
  selected?: boolean;
  onClick: (threat: Threat) => void;
  // kept in props for API compatibility; "show in architecture" lives in the detail panel
  onShowInArchitecture?: ((threat: Threat) => void) | undefined;
}

const SEVERITY_VARIANT: Record<string, "destructive" | "warning" | "secondary" | "outline"> = {
  critical: "destructive",
  high: "destructive",
  medium: "warning",
  low: "secondary",
  note: "outline",
};

const SEVERITY_LABEL: Record<string, string> = {
  critical: "Critical",
  high: "High",
  medium: "Medium",
  low: "Low",
  note: "Note",
};

export function ThreatCard({ threat, selected, onClick }: ThreatCardProps) {
  return (
    <div
      role="button"
      tabIndex={0}
      onClick={() => onClick(threat)}
      onKeyDown={(e) => {
        if (e.key === "Enter" || e.key === " ") {
          e.preventDefault();
          onClick(threat);
        }
      }}
      aria-pressed={selected}
      className={cn(
        "flex w-full cursor-pointer items-center gap-2 rounded-md border px-3 py-2 text-left transition-colors hover:bg-muted/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring",
        selected && "border-primary bg-primary/5",
      )}
    >
      <span className="shrink-0 rounded bg-muted px-1.5 py-0.5 text-xs font-mono font-bold">
        {threat.identifier}
      </span>

      <span className="min-w-0 flex-1 truncate text-sm font-medium leading-tight">
        {threat.title}
      </span>

      <div className="flex shrink-0 items-center gap-1.5">
        {threat.riskRating && (
          <Badge
            variant={SEVERITY_VARIANT[threat.riskRating.severity] ?? "secondary"}
            className="text-xs font-semibold"
          >
            {SEVERITY_LABEL[threat.riskRating.severity] ?? threat.riskRating.severity}
          </Badge>
        )}
        <FindingTypeBadge findingType={threat.findingType} className="text-xs" />
      </div>
    </div>
  );
}
