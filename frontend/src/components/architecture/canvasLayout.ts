import type { Node, Edge } from "reactflow";
import * as dagre from "@dagrejs/dagre";
import type { ArchitectureElement } from "@/api/architecture";
import type { ElementNodeData } from "./ElementNode";

const NODE_WIDTH = 180;
const NODE_HEIGHT = 80;
const TB_PADDING = 40; // space around contained elements inside a trust boundary box
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

  // Build name→TB id map so we can assign dagre parent relationships
  const nameLowerToTbId = new Map<string, string>();
  trustBoundaryElements.forEach((tb) => {
    const containedLabels = tb.properties?.["containedComponents"];
    const labels: string[] = Array.isArray(containedLabels)
      ? containedLabels.filter((v): v is string => typeof v === "string")
      : [];
    labels.forEach((l) => nameLowerToTbId.set(l.toLowerCase(), tb.id));
  });

  // Use a compound graph so dagre keeps each trust boundary's members clustered
  // together — prevents different boundaries' bounding boxes from overlapping.
  const g = new dagre.graphlib.Graph({ compound: true });
  g.setDefaultEdgeLabel(() => ({}));
  // ranksep/nodesep must absorb the visual TB_PADDING added after layout.
  // Vertical gap needed: 2*TB_PADDING + TB_LABEL_HEIGHT = 80 + 26 = 106 → use 130.
  // Horizontal gap needed: 2*TB_PADDING = 80 → use 110.
  g.setGraph({ rankdir: "LR", ranksep: 110, nodesep: 130 });

  // Add virtual container nodes for each trust boundary (dagre expands them to fit children)
  trustBoundaryElements.forEach((tb) => {
    g.setNode(tb.id, { width: NODE_WIDTH, height: NODE_HEIGHT });
  });

  // Add layout elements and assign them to their trust boundary parent (if any)
  layoutElements.forEach((e) => {
    g.setNode(e.id, { width: NODE_WIDTH, height: NODE_HEIGHT });
    const tbId = nameLowerToTbId.get(e.name.toLowerCase());
    if (tbId) g.setParent(e.id, tbId);
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

  // Build trust boundary nodes.
  // Prefer the dagre-computed compound bounds when available (they're guaranteed non-overlapping);
  // fall back to manual bounding box if dagre didn't expand the node (no matched children).
  const trustBoundaryNodes: Node<ElementNodeData>[] = [];
  for (const tb of trustBoundaryElements) {
    const tbPos = g.node(tb.id);

    // dagre expands the compound node beyond NODE_WIDTH×NODE_HEIGHT when it has children
    if (tbPos && (tbPos.width > NODE_WIDTH || tbPos.height > NODE_HEIGHT)) {
      const x = tbPos.x - tbPos.width / 2 - TB_PADDING;
      const y = tbPos.y - tbPos.height / 2 - TB_PADDING - TB_LABEL_HEIGHT;
      const width = tbPos.width + 2 * TB_PADDING;
      const height = tbPos.height + 2 * TB_PADDING + TB_LABEL_HEIGHT;
      trustBoundaryNodes.push({
        id: tb.id,
        type: "trustBoundary",
        position: { x, y },
        style: { width, height },
        data: { element: tb, threatCount: undefined, maxSeverity: null },
        zIndex: -1,
        draggable: false,
      });
      continue;
    }

    // Fallback: compute bounding box from matched member positions
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
