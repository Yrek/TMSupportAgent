import type { Node, Edge } from "reactflow";
import * as dagre from "@dagrejs/dagre";
import type { ArchitectureElement } from "@/api/architecture";
import type { ElementNodeData } from "./ElementNode";

const NODE_WIDTH = 180;
const NODE_HEIGHT = 80;
const TB_PADDING = 32; // space around contained elements inside a trust boundary box
const TB_LABEL_HEIGHT = 26; // vertical room reserved for the boundary label at the top

export function buildNodesAndEdges(
  elements: ArchitectureElement[],
  threatCountByElement?: Map<string, { count: number; maxSeverity: "critical" | "high" | "medium" | "low" | null }>,
  /** GAP-TH4: per-edge threat counts keyed by DataFlow element id */
  threatCountByEdge?: Map<string, number>,
): { nodes: Node<ElementNodeData>[]; edges: Edge[] } {
  const dataFlowElements = elements.filter((e) => e.elementType === "DataFlow");
  const trustBoundaryElements = elements.filter((e) => e.elementType === "TrustBoundary");
  // Elements that dagre lays out (everything except DataFlows and TrustBoundaries)
  const layoutElements = elements.filter(
    (e) => e.elementType !== "DataFlow" && e.elementType !== "TrustBoundary",
  );

  // Build a name→id map for resolving DataFlow from/to
  const nameToId = new Map<string, string>();
  layoutElements.forEach((e) => nameToId.set(e.name.toLowerCase(), e.id));

  // Auto-layout with dagre (TrustBoundaries excluded — positioned as bounding boxes later)
  const g = new dagre.graphlib.Graph();
  g.setDefaultEdgeLabel(() => ({}));
  g.setGraph({ rankdir: "LR", ranksep: 80, nodesep: 40 });

  layoutElements.forEach((e) => {
    g.setNode(e.id, { width: NODE_WIDTH, height: NODE_HEIGHT });
  });

  dataFlowElements.forEach((df) => {
    const fromProp = df.properties?.["from"];
    const toProp = df.properties?.["to"];
    const fromName = typeof fromProp === "string" ? fromProp.toLowerCase() : null;
    const toName = typeof toProp === "string" ? toProp.toLowerCase() : null;
    const fromId = fromName ? nameToId.get(fromName) : undefined;
    const toId = toName ? nameToId.get(toName) : undefined;
    if (fromId && toId) {
      g.setEdge(fromId, toId);
    }
  });

  dagre.layout(g);

  // Build regular element nodes and record their absolute positions for TB bounding-box computation
  const nameToPosition = new Map<string, { x: number; y: number }>();

  const regularNodes: Node<ElementNodeData>[] = layoutElements.map((element) => {
    const nodeWithPos = g.node(element.id);
    const threatInfo = threatCountByElement?.get(element.id);
    const x = (nodeWithPos?.x ?? 0) - NODE_WIDTH / 2;
    const y = (nodeWithPos?.y ?? 0) - NODE_HEIGHT / 2;

    nameToPosition.set(element.name.toLowerCase(), { x, y });

    return {
      id: element.id,
      type: "elementNode",
      position: { x, y },
      data: {
        element,
        threatCount: threatInfo?.count,
        maxSeverity: threatInfo?.maxSeverity ?? null,
      },
      zIndex: 0,
    };
  });

  // Build trust boundary nodes as bounding-box rectangles behind their contained elements
  const trustBoundaryNodes: Node<ElementNodeData>[] = [];
  for (const tb of trustBoundaryElements) {
    const containedLabels = tb.properties?.["containedComponents"];
    const labels: string[] = Array.isArray(containedLabels)
      ? containedLabels.filter((v): v is string => typeof v === "string")
      : [];

    const matchedPositions = labels
      .map((l) => nameToPosition.get(l.toLowerCase()))
      .filter((p): p is { x: number; y: number } => p !== undefined);

    if (matchedPositions.length === 0) {
      // No resolvable members — fall back to a regular node so it's still visible
      trustBoundaryNodes.push({
        id: tb.id,
        type: "elementNode",
        position: { x: 0, y: 0 },
        data: { element: tb, threatCount: undefined, maxSeverity: null },
        zIndex: 0,
      });
      continue;
    }

    const minX = Math.min(...matchedPositions.map((p) => p.x)) - TB_PADDING;
    const minY =
      Math.min(...matchedPositions.map((p) => p.y)) - TB_PADDING - TB_LABEL_HEIGHT;
    const maxX = Math.max(...matchedPositions.map((p) => p.x + NODE_WIDTH)) + TB_PADDING;
    const maxY = Math.max(...matchedPositions.map((p) => p.y + NODE_HEIGHT)) + TB_PADDING;

    trustBoundaryNodes.push({
      id: tb.id,
      type: "trustBoundary",
      position: { x: minX, y: minY },
      style: { width: maxX - minX, height: maxY - minY },
      data: { element: tb, threatCount: undefined, maxSeverity: null },
      // Render behind contained elements; selectable via the padding area
      zIndex: -1,
      draggable: false,
    });
  }

  // TrustBoundary nodes must appear first so React Flow renders them below other nodes
  const nodes: Node<ElementNodeData>[] = [...trustBoundaryNodes, ...regularNodes];

  const edges: Edge[] = [];
  dataFlowElements.forEach((df) => {
    const fromProp = df.properties?.["from"];
    const toProp = df.properties?.["to"];
    const fromName = typeof fromProp === "string" ? fromProp.toLowerCase() : null;
    const toName = typeof toProp === "string" ? toProp.toLowerCase() : null;
    const fromId = fromName ? nameToId.get(fromName) : undefined;
    const toId = toName ? nameToId.get(toName) : undefined;

    if (fromId && toId) {
      const edgeThreatCount = threatCountByEdge?.get(df.id) ?? 0;
      const edgeLabel = edgeThreatCount > 0
        ? `${df.name}  ⚠ ${edgeThreatCount}`
        : df.name;
      edges.push({
        id: df.id,
        source: fromId,
        target: toId,
        label: edgeLabel,
        type: "smoothstep",
        animated: false,
        style: {
          stroke: edgeThreatCount > 0 ? "#f59e0b" : "#94a3b8",
          strokeWidth: 2,
        },
        interactionWidth: 28,
        labelStyle: {
          fontSize: 10,
          fill: edgeThreatCount > 0 ? "#b45309" : "#64748b",
          fontWeight: edgeThreatCount > 0 ? 600 : 400,
        },
      });
    }
  });

  return { nodes, edges };
}
