import type { Threat } from "@/api/threats";
import { ThreatStatusBadge } from "./ThreatStatusBadge";
import { FindingTypeBadge } from "./FindingTypeBadge";
import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";

interface ThreatCardProps {
  threat: Threat;
  selected?: boolean;
  onClick: (threat: Threat) => void;
}

const CONFIDENCE_VARIANT: Record<string, "success" | "warning" | "destructive"> = {
  High: "success",
  Medium: "warning",
  Low: "destructive",
};

export function ThreatCard({ threat, selected, onClick }: ThreatCardProps) {
  return (
    <button
      onClick={() => onClick(threat)}
      className={cn(
        "w-full rounded-lg border p-4 text-left transition-colors hover:bg-muted/40",
        selected && "border-primary bg-primary/5",
      )}
    >
      <div className="flex items-start justify-between gap-3">
        <div className="flex items-center gap-2">
          <span className="shrink-0 rounded bg-muted px-1.5 py-0.5 text-xs font-mono font-bold">
            {threat.identifier}
          </span>
          <span className="font-medium leading-tight">{threat.title}</span>
        </div>
        <ThreatStatusBadge status={threat.status} />
      </div>

      <div className="mt-2 flex flex-wrap items-center gap-1.5">
        <Badge variant="outline" className="text-xs">{threat.methodCategory}</Badge>
        <Badge variant={CONFIDENCE_VARIANT[threat.confidence] ?? "secondary"} className="text-xs">
          {threat.confidence} confidence
        </Badge>
        <FindingTypeBadge findingType={threat.findingType} className="text-xs" />
      </div>

      <p className="mt-2 line-clamp-2 text-sm text-muted-foreground">
        {threat.description}
      </p>
    </button>
  );
}
