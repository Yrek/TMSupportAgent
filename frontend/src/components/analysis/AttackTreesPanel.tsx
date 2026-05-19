import { useState, useEffect, useRef, useId } from "react";
import { Copy, LayoutPanelTop, AlignLeft, ChevronDown, ChevronRight } from "lucide-react";
import { toast } from "sonner";
import { cn } from "@/lib/utils";
import mermaid from "mermaid";

mermaid.initialize({
  startOnLoad: false,
  theme: "neutral",
  // "loose" required for htmlLabels so <br/> in pre-processed labels renders correctly.
  // Diagrams are backend-generated, not direct user input.
  securityLevel: "loose",
  fontFamily: "inherit",
  flowchart: {
    htmlLabels: true,
    nodeSpacing: 12,
    rankSpacing: 20,
    padding: 4,
    diagramPadding: 4,
  },
  themeVariables: {
    fontSize: "8px",
  },
});

interface AttackTree {
  threatIdentifier: string;
  threatTitle: string;
  mermaidDiagram: string;
  textSummary: string;
}

interface AttackTreesPanelProps {
  attackTrees: AttackTree[];
  onThreatClick?: ((id: string) => void) | undefined;
}

const MERMAID_INIT =
  `%%{init: {"theme": "neutral", "themeVariables": {"fontSize": "8px"}, ` +
  `"flowchart": {"htmlLabels": true, "nodeSpacing": 12, "rankSpacing": 20, "padding": 4, "wrap": true, "diagramPadding": 4}}}%%\n`;

function insertLineBreaks(text: string, maxChars = 24): string {
  if (text.length <= maxChars) return text;
  const words = text.split(" ");
  const lines: string[] = [];
  let line = "";
  for (const word of words) {
    if (line && line.length + 1 + word.length > maxChars) {
      lines.push(line);
      line = word;
    } else {
      line = line ? `${line} ${word}` : word;
    }
  }
  if (line) lines.push(line);
  return lines.join("<br/>");
}

function prewrapMermaidLabels(source: string): string {
  return source
    .replace(/\["([^"]+)"\]/g, (_, label) => `["${insertLineBreaks(label)}"]`)
    .replace(/\("([^"]+)"\)/g, (_, label) => `("${insertLineBreaks(label)}")`);
}

function MermaidDiagram({ chart }: { chart: string }) {
  const containerRef = useRef<HTMLDivElement>(null);
  const uid = useId().replace(/:/g, "");
  const id = `mermaid-${uid}`;

  useEffect(() => {
    let cancelled = false;
    mermaid
      .render(id, MERMAID_INIT + prewrapMermaidLabels(chart))
      .then(({ svg }) => {
        if (!cancelled && containerRef.current) {
          containerRef.current.innerHTML = svg;
          const svgEl = containerRef.current.querySelector("svg");
          if (svgEl) {
            svgEl.removeAttribute("width");
            svgEl.removeAttribute("height");
            svgEl.style.maxWidth = "min(100%, 560px)";
            svgEl.style.height = "auto";
            svgEl.style.fontSize = "8px";
          }
        }
      })
      .catch(() => {
        if (!cancelled && containerRef.current) {
          containerRef.current.innerHTML =
            '<p class="text-xs text-destructive p-2">Diagram render failed — copy Mermaid source to view.</p>';
        }
      });
    return () => {
      cancelled = true;
    };
  }, [chart, id]);

  return <div ref={containerRef} className="flex justify-center overflow-x-auto py-2 [&>svg]:max-w-[560px] [&>svg]:h-auto" />;
}

function AttackTreeRow({
  tree,
  onThreatClick,
}: {
  tree: AttackTree;
  onThreatClick?: ((id: string) => void) | undefined;
}) {
  const [open, setOpen] = useState(true);
  const [view, setView] = useState<"diagram" | "text">("diagram");

  return (
    <div className="rounded-lg border">
      {/* Header */}
      <div
        role="button"
        tabIndex={0}
        className="flex w-full items-center gap-2 px-4 py-3 text-left hover:bg-muted/30 transition-colors cursor-pointer"
        onClick={() => setOpen((v) => !v)}
        onKeyDown={(e) => { if (e.key === "Enter" || e.key === " ") setOpen((v) => !v); }}
      >
        {open ? (
          <ChevronDown className="h-4 w-4 shrink-0 text-muted-foreground" />
        ) : (
          <ChevronRight className="h-4 w-4 shrink-0 text-muted-foreground" />
        )}
        <button
          onClick={(e) => {
            e.stopPropagation();
            onThreatClick?.(tree.threatIdentifier);
          }}
          className="rounded bg-muted px-1.5 py-0.5 text-xs font-mono font-bold hover:bg-primary/10 hover:text-primary transition-colors shrink-0"
        >
          {tree.threatIdentifier}
        </button>
        <span className="flex-1 truncate text-sm font-medium">{tree.threatTitle}</span>
        {/* View toggle */}
        {open && (
          <span
            className="flex items-center gap-0.5 rounded-md border text-xs overflow-hidden shrink-0"
            onClick={(e) => e.stopPropagation()}
          >
            <button
              onClick={() => setView("diagram")}
              title="Diagram view"
              className={cn(
                "flex items-center gap-1 px-2 py-1 transition-colors",
                view === "diagram"
                  ? "bg-muted text-foreground"
                  : "text-muted-foreground hover:text-foreground",
              )}
            >
              <LayoutPanelTop className="h-3 w-3" />
              Diagram
            </button>
            <button
              onClick={() => setView("text")}
              title="Text view"
              className={cn(
                "flex items-center gap-1 px-2 py-1 transition-colors border-l",
                view === "text"
                  ? "bg-muted text-foreground"
                  : "text-muted-foreground hover:text-foreground",
              )}
            >
              <AlignLeft className="h-3 w-3" />
              Text
            </button>
          </span>
        )}
        {/* Copy Mermaid */}
        <button
          title="Copy Mermaid source"
          onClick={(e) => {
            e.stopPropagation();
            void navigator.clipboard.writeText(tree.mermaidDiagram);
            toast.success("Mermaid source copied");
          }}
          className="shrink-0 rounded p-1 text-muted-foreground hover:bg-muted hover:text-foreground transition-colors"
        >
          <Copy className="h-3.5 w-3.5" />
        </button>
      </div>

      {/* Body */}
      {open && (
        <div className="border-t px-4 pb-4 pt-3">
          {view === "diagram" ? (
            <MermaidDiagram chart={tree.mermaidDiagram} />
          ) : (
            <pre className="whitespace-pre-wrap rounded-md bg-muted/40 p-3 text-xs font-mono leading-relaxed">
              {tree.textSummary}
            </pre>
          )}
        </div>
      )}
    </div>
  );
}

export function AttackTreesPanel({ attackTrees, onThreatClick }: AttackTreesPanelProps) {
  if (!attackTrees.length) {
    return (
      <div className="flex items-center justify-center p-12 text-center text-muted-foreground text-sm">
        No attack trees generated — only high and critical severity threats produce trees.
      </div>
    );
  }

  function copyAll() {
    const text = attackTrees
      .map((t) => `# ${t.threatIdentifier}: ${t.threatTitle}\n\n${t.mermaidDiagram}`)
      .join("\n\n---\n\n");
    void navigator.clipboard.writeText(text);
    toast.success(`${attackTrees.length} attack tree${attackTrees.length !== 1 ? "s" : ""} copied`);
  }

  return (
    <div className="p-4 space-y-3">
      <div className="flex items-center justify-between">
        <p className="text-sm text-muted-foreground">
          {attackTrees.length} attack tree{attackTrees.length !== 1 ? "s" : ""} for high/critical threats
        </p>
        <button
          onClick={copyAll}
          className={cn(
            "flex items-center gap-1.5 rounded-md border px-3 py-1.5 text-xs font-medium transition-colors",
            "text-muted-foreground hover:bg-muted hover:text-foreground",
          )}
        >
          <Copy className="h-3.5 w-3.5" />
          Copy all Mermaid
        </button>
      </div>
      {attackTrees.map((tree) => (
        <AttackTreeRow key={tree.threatIdentifier} tree={tree} onThreatClick={onThreatClick} />
      ))}
    </div>
  );
}
