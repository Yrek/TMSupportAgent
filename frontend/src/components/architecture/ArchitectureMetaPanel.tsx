import { useState } from "react";
import { ChevronDown, ChevronRight, Check, HelpCircle } from "lucide-react";
import type { ArchitectureModel } from "@/api/architecture";
import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";

const PRIORITY_VARIANT: Record<string, "destructive" | "warning" | "secondary"> = {
  high: "destructive",
  medium: "warning",
  low: "secondary",
};

interface ArchitectureMetaPanelProps {
  architecture: ArchitectureModel;
}

export function ArchitectureMetaPanel({ architecture }: ArchitectureMetaPanelProps) {
  const [expanded, setExpanded] = useState(false);

  return (
    <div className="border-b">
      <button
        onClick={() => setExpanded(!expanded)}
        className="flex w-full items-center justify-between px-4 py-2 text-sm font-medium hover:bg-muted/50 transition-colors"
      >
        <span>Architecture details</span>
        {expanded ? <ChevronDown className="h-4 w-4" /> : <ChevronRight className="h-4 w-4" />}
      </button>

      {expanded && (
        <div className="px-4 pb-4 space-y-4">
          {/* System purpose */}
          {architecture.systemPurpose && (
            <div>
              <p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground mb-1">
                System purpose
              </p>
              <p className="text-sm">{architecture.systemPurpose}</p>
            </div>
          )}

          {/* Classification */}
          {architecture.classification.length > 0 && (
            <div>
              <p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground mb-1">
                Classification
              </p>
              <div className="flex flex-wrap gap-1">
                {architecture.classification.map((c) => (
                  <Badge key={c} variant="outline">
                    {c}
                  </Badge>
                ))}
              </div>
            </div>
          )}

          {/* Assumptions */}
          {architecture.assumptions.length > 0 && (
            <div>
              <p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground mb-1">
                Assumptions
              </p>
              <ul className="space-y-1">
                {architecture.assumptions.map((a, idx) => (
                  <li key={idx} className="flex items-start gap-2 text-sm">
                    <Check
                      className={cn(
                        "mt-0.5 h-3.5 w-3.5 shrink-0",
                        a.confirmed ? "text-green-500" : "text-muted-foreground",
                      )}
                    />
                    <span className={a.confirmed ? "" : "text-muted-foreground"}>{a.text}</span>
                  </li>
                ))}
              </ul>
            </div>
          )}

          {/* Gaps */}
          {architecture.gaps.length > 0 && (
            <div>
              <p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground mb-1">
                Identified gaps
              </p>
              <ul className="space-y-1 text-sm text-muted-foreground list-disc list-inside">
                {architecture.gaps.map((g, idx) => (
                  <li key={idx}>{g}</li>
                ))}
              </ul>
            </div>
          )}

          {/* Clarification questions */}
          {architecture.clarificationQuestions.length > 0 && (
            <div>
              <p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground mb-1">
                Clarification questions
              </p>
              <div className="space-y-2">
                {architecture.clarificationQuestions.map((q, idx) => (
                  <div key={idx} className="flex items-start gap-2">
                    <HelpCircle className="mt-0.5 h-3.5 w-3.5 shrink-0 text-muted-foreground" />
                    <div className="flex-1">
                      <p className="text-sm">{q.question}</p>
                      <Badge
                        variant={PRIORITY_VARIANT[q.priority.toLowerCase()] ?? "secondary"}
                        className="mt-0.5 text-[10px]"
                      >
                        {q.priority}
                      </Badge>
                    </div>
                  </div>
                ))}
              </div>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
