import { useCallback, useMemo } from "react";
import ReactFlow, {
  Background,
  Controls,
  MiniMap,
  type NodeTypes,
  type NodeMouseHandler,
} from "reactflow";
import "reactflow/dist/style.css";
import type { ArchitectureElement } from "@/api/architecture";
import { ElementNode, type ElementNodeData } from "./ElementNode";
import { buildNodesAndEdges } from "./canvasLayout";

const NODE_TYPES: NodeTypes = { elementNode: ElementNode };

interface ThreatInfo {
  count: number;
  maxSeverity: "critical" | "high" | "medium" | "low" | null;
}

interface ArchCanvasProps {
  elements: ArchitectureElement[];
  readOnly?: boolean;
  onElementSelect?: (element: ArchitectureElement | null) => void;
  selectedElementId?: string | null | undefined;
  threatCountByElement?: Map<string, ThreatInfo>;
  /** GAP-TH4: per-edge threat counts keyed by DataFlow element id */
  threatCountByEdge?: Map<string, number>;
  /** GAP-TH4: called when a DataFlow edge is clicked */
  onEdgeClick?: (edgeElementId: string) => void;
  /** F-904: called when Delete key is pressed on a selected UserAdded element */
  onDeleteElement?: (elementId: string) => void;
}

export function ArchCanvas({
  elements,
  readOnly = false,
  onElementSelect,
  selectedElementId,
  threatCountByElement,
  threatCountByEdge,
  onEdgeClick,
  onDeleteElement,
}: ArchCanvasProps) {
  // Rebuild layout when elements change (new elements added)
  const nodesWithSelection = useMemo(() => {
    const updated = buildNodesAndEdges(elements, threatCountByElement, threatCountByEdge);
    return updated.nodes.map((n) => ({
      ...n,
      selected: n.id === selectedElementId,
    }));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [elements, selectedElementId, threatCountByEdge]);

  const edgesFromElements = useMemo(
    () => buildNodesAndEdges(elements, threatCountByElement, threatCountByEdge).edges,
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [elements, threatCountByEdge],
  );

  const handleEdgeClick = useCallback(
    (_event: unknown, edge: { id: string }) => {
      onEdgeClick?.(edge.id);
    },
    [onEdgeClick],
  );

  const handleNodeClick: NodeMouseHandler = useCallback(
    (_event, node) => {
      if (!onElementSelect) return;
      const el = elements.find((e) => e.id === node.id);
      onElementSelect(el ?? null);
    },
    [elements, onElementSelect],
  );

  const handlePaneClick = useCallback(() => {
    onElementSelect?.(null);
  }, [onElementSelect]);

  // F-904: Navigable elements are non-DataFlow nodes (DataFlows render as edges)
  const navigableElements = useMemo(
    () => elements.filter((e) => e.elementType !== "DataFlow"),
    [elements],
  );

  const handleKeyDown = useCallback(
    (e: { key: string; shiftKey: boolean; preventDefault: () => void }) => {
      if (navigableElements.length === 0) return;

      const currentIdx = navigableElements.findIndex((el) => el.id === selectedElementId);

      if (e.key === "Tab") {
        e.preventDefault();
        const nextIdx = e.shiftKey
          ? currentIdx <= 0
            ? navigableElements.length - 1
            : currentIdx - 1
          : currentIdx >= navigableElements.length - 1
          ? 0
          : currentIdx + 1;
        onElementSelect?.(navigableElements[nextIdx] ?? null);
      } else if (e.key === "Enter") {
        e.preventDefault();
        if (currentIdx < 0 && navigableElements.length > 0) {
          // Nothing selected — select first
          onElementSelect?.(navigableElements[0] ?? null);
        }
        // If already selected, detail panel is already open — no-op
      } else if (e.key === "Delete" && !readOnly && currentIdx >= 0) {
        const el = navigableElements[currentIdx];
        if (el?.source === "UserAdded" && onDeleteElement) {
          onDeleteElement(el.id);
        }
      }
    },
    [navigableElements, selectedElementId, onElementSelect, readOnly, onDeleteElement],
  );

  return (
    <div
      className="h-full w-full"
      tabIndex={0}
      onKeyDown={handleKeyDown}
      aria-label="Architecture diagram. Use Tab to cycle elements, Enter to select, Delete to remove a user-added element."
    >
      <ReactFlow
        nodes={nodesWithSelection}
        edges={edgesFromElements}
        onNodesChange={undefined}
        onEdgesChange={undefined}
        onNodeClick={handleNodeClick}
        onEdgeClick={handleEdgeClick}
        onPaneClick={handlePaneClick}
        nodeTypes={NODE_TYPES}
        nodesDraggable={!readOnly}
        nodesConnectable={false}
        elementsSelectable={true}
        fitView
        fitViewOptions={{ padding: 0.2 }}
        minZoom={0.3}
        maxZoom={2}
        attributionPosition="bottom-left"
      >
        <Background gap={16} size={1} />
        <Controls showInteractive={false} />
        <MiniMap
          nodeColor={(n) => {
            const data = n.data as ElementNodeData | undefined;
            if (!data?.element) return "#e2e8f0";
            return "#e2e8f0";
          }}
          zoomable
          pannable
        />
      </ReactFlow>
    </div>
  );
}
