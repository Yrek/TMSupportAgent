import { useState } from "react";
import type { Threat } from "@/api/threats";
import { FindingTypeBadge } from "./FindingTypeBadge";
import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";
import type { ThreatStatus } from "@/lib/constants";
import { Check, ArrowRight, X, MessageSquare } from "lucide-react";

interface ThreatCardProps {
  threat: Threat;
  selected?: boolean;
  onClick: (threat: Threat) => void;
  onShowInArchitecture?: ((threat: Threat) => void) | undefined;
  onUpdateStatus?: (id: string, status: ThreatStatus) => void;
}

const SEVERITY_BORDER: Record<string, string> = {
  critical: "border-l-red-600",
  high:     "border-l-orange-500",
  medium:   "border-l-amber-400",
  low:      "border-l-blue-400",
  note:     "border-l-border",
};

const SEVERITY_VARIANT: Record<string, "destructive" | "warning" | "secondary" | "outline"> = {
  critical: "destructive",
  high:     "destructive",
  medium:   "warning",
  low:      "secondary",
  note:     "outline",
};

const SEVERITY_LABEL: Record<string, string> = {
  critical: "Critical",
  high:     "High",
  medium:   "Medium",
  low:      "Low",
  note:     "Note",
};

export function ThreatCard({ threat, selected, onClick, onUpdateStatus }: ThreatCardProps) {
  const [hovering, setHovering] = useState(false);
  const severity = threat.riskRating?.severity ?? "note";
  const isOpen = threat.status === "Open";

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
      onMouseEnter={() => setHovering(true)}
      onMouseLeave={() => setHovering(false)}
      aria-pressed={selected}
      className={cn(
        "flex w-full cursor-pointer items-center gap-2 rounded-md border border-l-4 px-3 py-2 text-left transition-colors hover:bg-muted/40 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring",
        SEVERITY_BORDER[severity] ?? "border-l-border",
        selected && "border-primary bg-primary/5",
        threat.status === "Rejected" && "opacity-50",
      )}
    >
      {/* Status dot */}
      <span
        title={threat.status}
        className={cn(
          "h-2 w-2 shrink-0 rounded-full",
          threat.status === "Accepted"  ? "bg-green-500" :
          threat.status === "Mitigated" ? "bg-blue-500"  :
          threat.status === "Rejected"  ? "bg-muted-foreground/30" :
                                          "bg-muted-foreground/20",
        )}
      />

      <span className="shrink-0 rounded bg-muted px-1.5 py-0.5 text-[11px] font-mono font-bold">
        {threat.identifier}
      </span>

      <span className="min-w-0 flex-1 truncate text-sm font-medium leading-tight">
        {threat.title}
      </span>

      {/* Quick triage actions — appear on hover for Open threats */}
      {onUpdateStatus && isOpen && hovering ? (
        <div
          className="flex shrink-0 items-center gap-0.5"
          onClick={(e) => e.stopPropagation()}
        >
          <button
            title="Accept"
            onClick={() => onUpdateStatus(threat.id, "Accepted")}
            className="rounded p-1 text-green-600 hover:bg-green-50 dark:hover:bg-green-950 transition-colors"
          >
            <Check className="h-3.5 w-3.5" />
          </button>
          <button
            title="Mitigate"
            onClick={() => onUpdateStatus(threat.id, "Mitigated")}
            className="rounded p-1 text-blue-600 hover:bg-blue-50 dark:hover:bg-blue-950 transition-colors"
          >
            <ArrowRight className="h-3.5 w-3.5" />
          </button>
          <button
            title="Reject"
            onClick={() => onUpdateStatus(threat.id, "Rejected")}
            className="rounded p-1 text-muted-foreground hover:bg-muted transition-colors"
          >
            <X className="h-3.5 w-3.5" />
          </button>
        </div>
      ) : (
        <div className="flex shrink-0 items-center gap-1.5">
          {(threat.notes?.length ?? 0) > 0 && (
            <span title={`${threat.notes.length} note${threat.notes.length !== 1 ? "s" : ""}`}>
              <MessageSquare className="h-3.5 w-3.5 text-muted-foreground/60" />
            </span>
          )}
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
      )}
    </div>
  );
}
