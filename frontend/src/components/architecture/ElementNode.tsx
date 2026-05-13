import { memo } from "react";
import { Handle, Position, type NodeProps } from "reactflow";
import { cn } from "@/lib/utils";
import type { ArchitectureElement } from "@/api/architecture";
import { ELEMENT_TYPE_CONFIG } from "./elementTypeConfig";

export interface ElementNodeData {
  element: ArchitectureElement;
  selected?: boolean | undefined;
  threatCount?: number | undefined;
  maxSeverity?: "critical" | "high" | "medium" | "low" | null;
  drawFlowMode?: boolean | undefined;
}

const CONFIDENCE_COLOR: Record<string, string> = {
  High: "bg-green-500",
  Medium: "bg-yellow-500",
  Low: "bg-red-500",
};

const SEVERITY_BORDER: Record<string, string> = {
  critical: "ring-2 ring-red-600",
  high: "ring-2 ring-orange-500",
  medium: "ring-2 ring-yellow-500",
  low: "ring-2 ring-blue-400",
};

const SEVERITY_BG: Record<string, string> = {
  critical: "bg-red-600",
  high: "bg-orange-500",
  medium: "bg-amber-400",
  low: "bg-blue-400",
};

const SEVERITY_ABBR: Record<string, string> = {
  critical: "CRIT",
  high: "HIGH",
  medium: "MED",
  low: "LOW",
};

export const ElementNode = memo(function ElementNode({ data, selected }: NodeProps<ElementNodeData>) {
  const { element, threatCount, maxSeverity, drawFlowMode } = data;
  const config = ELEMENT_TYPE_CONFIG[element.elementType];

  return (
    <div
      className={cn(
        "min-w-[120px] max-w-[180px] rounded-lg border-2 bg-card p-3 shadow-sm transition-all",
        config.borderClass,
        selected && "ring-2 ring-primary ring-offset-1",
        maxSeverity && SEVERITY_BORDER[maxSeverity],
      )}
    >
      {/* Header row */}
      <div className="flex items-start gap-2">
        <span className="mt-0.5 shrink-0 text-lg">{config.icon}</span>
        <div className="flex-1 min-w-0">
          <p className="truncate text-sm font-medium leading-tight">{element.name}</p>
          <p className={cn("text-xs font-medium", config.textClass)}>{config.label}</p>
        </div>
      </div>

      {/* Source + confidence */}
      <div className="mt-2 flex items-center gap-1.5">
        <span
          className={cn(
            "rounded-full px-1.5 py-0.5 text-[10px] font-medium",
            element.source === "Extracted"
              ? "bg-blue-100 text-blue-700"
              : "bg-purple-100 text-purple-700",
          )}
        >
          {element.source === "Extracted" ? "Extracted" : "Added"}
        </span>

        {element.extractionConfidence && (
          <span
            className={cn(
              "h-2 w-2 rounded-full",
              CONFIDENCE_COLOR[element.extractionConfidence] ?? "bg-gray-400",
            )}
            title={`Confidence: ${element.extractionConfidence}`}
          />
        )}

        {threatCount !== undefined && threatCount > 0 && (
          <span
            className={cn(
              "ml-auto rounded px-1.5 py-0.5 text-[10px] font-bold text-white",
              maxSeverity ? SEVERITY_BG[maxSeverity] : "bg-slate-500",
            )}
          >
            {maxSeverity ? `${SEVERITY_ABBR[maxSeverity]} · ${threatCount}` : threatCount}
          </span>
        )}
      </div>

      <Handle
        id="in-left"
        type="target"
        position={Position.Left}
        className={cn(
          "!h-2.5 !w-2.5 !border !border-muted-foreground/40 !bg-background transition-opacity",
          drawFlowMode ? "!opacity-100" : "!opacity-0",
        )}
      />
      <Handle
        id="out-right"
        type="source"
        position={Position.Right}
        className={cn(
          "!h-2.5 !w-2.5 !border !border-muted-foreground/40 !bg-background transition-opacity",
          drawFlowMode ? "!opacity-100" : "!opacity-0",
        )}
      />
    </div>
  );
});
