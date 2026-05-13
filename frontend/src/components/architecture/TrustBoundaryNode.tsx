import { memo } from "react";
import { ShieldCheck } from "lucide-react";
import type { NodeProps } from "reactflow";
import { cn } from "@/lib/utils";
import type { ElementNodeData } from "./ElementNode";

export const TrustBoundaryNode = memo(function TrustBoundaryNode({
  data,
  selected,
}: NodeProps<ElementNodeData>) {
  return (
    <div
      className={cn(
        "relative h-full w-full rounded-xl border-2 border-dashed transition-colors",
        selected
          ? "border-red-500 bg-red-100/30"
          : "border-red-400/70 bg-red-50/20 dark:bg-red-900/10",
      )}
    >
      <div className="absolute left-3 top-2.5 flex items-center gap-1 text-red-700 dark:text-red-400">
        <ShieldCheck className="h-3.5 w-3.5 shrink-0" />
        <span className="select-none text-xs font-semibold leading-none">
          {data.element.name}
        </span>
      </div>
    </div>
  );
});
