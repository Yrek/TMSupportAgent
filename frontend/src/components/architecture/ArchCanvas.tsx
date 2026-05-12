import { useCallback, useEffect, useMemo } from "react";
import ReactFlow, {
  Background,
  Controls,
  MiniMap,
  useNodesState,
  type Connection,
  type NodeTypes,
  type NodeMouseHandler,
} from "reactflow";
import "reactflow/dist/style.css";
import type { ArchitectureElement } from "@/api/architecture";
import { ElementNode, type ElementNodeData } from "./ElementNode";
import { TrustBoundaryNode } from "./TrustBoundaryNode";
import { buildNodesAndEdges } from "./canvasLayout";

const NODE_TYPES: NodeTypes = { elementNode: ElementNode, trustBoundary: TrustBoundaryNode };

interface ThreatInfo {
  count: number;
  maxSeverity: "critical" | "high" | "medium" | "low" | null;
}

interface ArchCanvasProps {
  elements: ArchitectureElement[];
  readOnly?: boolean;
  drawFlowMode?: boolean;
  onElementSelect?: (element: ArchitectureElement | null) => void;
  selectedElementId?: string | null | undefined;
  threatCountByElement?: Map<string, ThreatInfo>;
  /** GAP-TH4: per-edge threat counts keyed by DataFlow element id */
  threatCountByEdge?: Map<string, number>;
  /** GAP-TH4: called when a DataFlow edge is clicked */
  onEdgeClick?: (edgeElementId: string) => void;
  /** F-904: called when Delete key is pressed on a selected element */
  onDeleteElement?: (elementId: string) => void;
  /** Called in review mode when user draws a line between two nodes */
  onCreateDataFlow?: ((from: ArchitectureElement, to: ArchitectureElement) => void | Promise<void>) | undefined;
}

export function ArchCanvas({
  elements,
  readOnly = false,
  drawFlowMode = false,
  onElementSelect,
  selectedElementId,
  threatCountByElement,
  threatCountByEdge,
  onEdgeClick,
  onDeleteElement,
  onCreateDataFlow,
}: ArchCanvasProps) {
  // Compute full layout (nodes + edges) whenever elements or threat info changes.
  // Memoised so we don't call dagre on every render.
  const layout = useMemo(
    () => buildNodesAndEdges(elements, threatCountByElement, threatCountByEdge),
    [elements, threatCountByElement, threatCountByEdge],
  );

  // useNodesState gives us drag-position tracking for free.
  // Initialised once; synced via useEffect when the layout changes (new elements added, etc.).
  const [nodes, setNodes, onNodesChange] = useNodesState<ElementNodeData>(layout.nodes);

  useEffect(() => {
    setNodes(layout.nodes);
  }, [layout.nodes, setNodes]);

  // Overlay selection and drawFlowMode without touching stored positions.
  const nodesWithSelection = useMemo(
    () =>
      nodes.map((n) => ({
        ...n,
        data: { ...n.data, drawFlowMode: !readOnly && drawFlowMode },
        selected: n.id === selectedElementId,
      })),
    [nodes, selectedElementId, readOnly, drawFlowMode],
  );

  const edgesWithSelection = useMemo(
    () =>
      layout.edges.map((e) => {
        if (e.id !== selectedElementId) return e;
        return {
          ...e,
          animated: true,
          style: { ...(e.style ?? {}), stroke: "#2563eb", strokeWidth: 3 },
          labelStyle: { ...(e.labelStyle ?? {}), fill: "#1d4ed8", fontWeight: 700 },
        };
      }),
    [layout.edges, selectedElementId],
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

  const handleConnect = useCallback(
    async (connection: Connection) => {
      if (readOnly || !drawFlowMode || !onCreateDataFlow) return;
      if (!connection.source || !connection.target || connection.source === connection.target) return;

      const from = elements.find((e) => e.id === connection.source);
      const to = elements.find((e) => e.id === connection.target);
      if (!from || !to) return;

      await onCreateDataFlow(from, to);
    },
    [readOnly, drawFlowMode, onCreateDataFlow, elements],
  );

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
          onElementSelect?.(navigableElements[0] ?? null);
        }
      } else if (e.key === "Delete" && !readOnly && currentIdx >= 0) {
        const el = navigableElements[currentIdx];
        if (el && onDeleteElement) {
          onDeleteElement(el.id);
        }
      }
    },
    [navigableElements, selectedElementId, onElementSelect, readOnly, onDeleteElement],
  );

  return (
    <div
      className="h-full w-full"
      role="button"
      tabIndex={0}
      onClick={() => {}}
      onKeyDown={handleKeyDown}
      aria-label="Architecture diagram. Use Tab to cycle elements, Enter to select, Delete to remove the selected element."
    >
      <ReactFlow
        nodes={nodesWithSelection}
        edges={edgesWithSelection}
        onNodesChange={onNodesChange}
        onEdgesChange={undefined}
        onNodeClick={handleNodeClick}
        onEdgeClick={handleEdgeClick}
        onPaneClick={handlePaneClick}
        onConnect={handleConnect}
        nodeTypes={NODE_TYPES}
        nodesDraggable={!readOnly}
        nodesConnectable={!readOnly && drawFlowMode}
        elementsSelectable={true}
        fitView
        fitViewOptions={{ padding: 0.08 }}
        minZoom={0.5}
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
