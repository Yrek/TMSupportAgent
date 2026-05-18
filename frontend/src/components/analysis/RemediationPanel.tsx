import { Badge } from "@/components/ui/badge";
import { Copy } from "lucide-react";
import { toast } from "sonner";

type Priority = "critical" | "high" | "medium" | "low";

interface RemediationItem {
  threatIdentifier: string;
  title: string;
  mitigationSummary: string;
  priority: Priority;
}

interface RemediationPanelProps {
  items: RemediationItem[];
  onThreatClick?: (identifier: string) => void;
}

const PRIORITY_ORDER: Priority[] = ["critical", "high", "medium", "low"];
const PRIORITY_VARIANT: Record<Priority, "destructive" | "warning" | "secondary" | "outline"> = {
  critical: "destructive",
  high: "destructive",
  medium: "warning",
  low: "secondary",
};

export function RemediationPanel({ items, onThreatClick }: RemediationPanelProps) {
  if (!items.length) {
    return (
      <div className="flex items-center justify-center p-12 text-center text-muted-foreground text-sm">
        No prioritized remediation items.
      </div>
    );
  }

  const grouped = new Map<Priority, RemediationItem[]>();
  PRIORITY_ORDER.forEach((p) => {
    const group = items.filter((i) => i.priority.toLowerCase() === p);
    if (group.length > 0) grouped.set(p, group);
  });

  return (
    <div className="space-y-6 p-4">
      {Array.from(grouped.entries()).map(([priority, group]) => (
        <div key={priority}>
          <div className="mb-2 flex items-center gap-2">
            <Badge variant={PRIORITY_VARIANT[priority]} className="capitalize">{priority}</Badge>
            <span className="text-xs text-muted-foreground">({group.length} items)</span>
          </div>
          <div className="space-y-2">
            {group.map((item, idx) => (
              <div key={idx} className="rounded-lg border p-3 space-y-1">
                <div className="flex items-start gap-2">
                  <div className="flex min-w-0 flex-1 flex-wrap items-center gap-2">
                    <button
                      onClick={() => onThreatClick?.(item.threatIdentifier)}
                      className="rounded bg-muted px-1.5 py-0.5 text-xs font-mono font-bold hover:bg-primary/10 hover:text-primary transition-colors shrink-0"
                    >
                      {item.threatIdentifier}
                    </button>
                    <span className="text-sm font-medium">{item.title}</span>
                  </div>
                  <button
                    title="Copy to clipboard"
                    onClick={() => {
                      void navigator.clipboard.writeText(`${item.threatIdentifier}: ${item.title}\n${item.mitigationSummary}`);
                      toast.success("Copied");
                    }}
                    className="shrink-0 rounded p-1 text-muted-foreground hover:bg-muted hover:text-foreground transition-colors"
                  >
                    <Copy className="h-3.5 w-3.5" />
                  </button>
                </div>
                <p className="text-sm text-muted-foreground">{item.mitigationSummary}</p>
              </div>
            ))}
          </div>
        </div>
      ))}
    </div>
  );
}
