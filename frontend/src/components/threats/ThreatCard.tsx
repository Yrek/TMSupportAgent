import type { Threat } from "@/api/threats";
import { ThreatStatusBadge } from "./ThreatStatusBadge";
import { FindingTypeBadge } from "./FindingTypeBadge";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";

interface ThreatCardProps {
  threat: Threat;
  selected?: boolean;
  onClick: (threat: Threat) => void;
  onShowInArchitecture?: ((threat: Threat) => void) | undefined;
}

const CONFIDENCE_VARIANT: Record<string, "success" | "warning" | "destructive"> = {
  High: "success",
  Medium: "warning",
  Low: "destructive",
};

export function ThreatCard({ threat, selected, onClick, onShowInArchitecture }: ThreatCardProps) {
  const canShowInArchitecture =
    typeof onShowInArchitecture === "function" && threat.affectedElementIds.length > 0;

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
        "w-full cursor-pointer rounded-lg border p-4 text-left transition-colors hover:bg-muted/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring",
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
        {Array.isArray(threat.sourceMethods) && threat.sourceMethods.length > 0 && (
          <Badge variant="secondary" className="text-xs">
            {threat.sourceMethods.length > 1
              ? `${threat.sourceMethods.length} methods`
              : threat.sourceMethods[0]}
          </Badge>
        )}
        <Badge variant={CONFIDENCE_VARIANT[threat.confidence] ?? "secondary"} className="text-xs">
          {threat.confidence} confidence
        </Badge>
        <FindingTypeBadge findingType={threat.findingType} className="text-xs" />
      </div>

      <p className="mt-2 line-clamp-2 text-sm text-muted-foreground">
        {threat.description}
      </p>

      {canShowInArchitecture && (
        <div className="mt-3 flex justify-end">
          <Button
            type="button"
            size="sm"
            variant="outline"
            onClick={(e) => {
              e.stopPropagation();
              onShowInArchitecture?.(threat);
            }}
          >
            Show in architecture
          </Button>
        </div>
      )}
    </div>
  );
}
