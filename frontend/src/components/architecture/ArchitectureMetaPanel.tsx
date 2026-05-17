import { useState } from "react";
import { ChevronDown, ChevronRight, Check, HelpCircle, CheckCircle2 } from "lucide-react";
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
  onGoToQuestions?: () => void;
}

export function ArchitectureMetaPanel({ architecture, onGoToQuestions }: ArchitectureMetaPanelProps) {
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

          {/* Clarification questions — summary only, full Q&A in Questions tab */}
          {architecture.clarificationQuestions.length > 0 && (
            <div>
              <p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground mb-1">
                Clarification questions
              </p>
              {(() => {
                const answeredCount = architecture.clarificationAnswers.filter(
                  (a) => a.answer.trim().length > 0,
                ).length;
                const total = architecture.clarificationQuestions.length;
                const allAnswered = answeredCount >= total;
                return (
                  <button
                    onClick={onGoToQuestions}
                    className="flex items-center gap-2 text-sm text-muted-foreground hover:text-foreground transition-colors"
                  >
                    {allAnswered ? (
                      <CheckCircle2 className="h-3.5 w-3.5 shrink-0 text-green-500" />
                    ) : (
                      <HelpCircle className="h-3.5 w-3.5 shrink-0 text-amber-500" />
                    )}
                    <span>
                      {allAnswered
                        ? `${total} question${total !== 1 ? "s" : ""} — all answered`
                        : `${total} question${total !== 1 ? "s" : ""} — ${total - answeredCount} unanswered`}
                    </span>
                  </button>
                );
              })()}
            </div>
          )}
        </div>
      )}
    </div>
  );
}
