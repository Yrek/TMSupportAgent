import type { Node, Edge } from "reactflow";
import * as dagre from "@dagrejs/dagre";
import type { ArchitectureElement } from "@/api/architecture";
import type { ElementNodeData } from "./ElementNode";

const NODE_WIDTH = 180;
const NODE_HEIGHT = 80;

export function buildNodesAndEdges(
  elements: ArchitectureElement[],
  threatCountByElement?: Map<string, { count: number; maxSeverity: "critical" | "high" | "medium" | "low" | null }>,
  /** GAP-TH4: per-edge threat counts keyed by DataFlow element id */
  threatCountByEdge?: Map<string, number>,
): { nodes: Node<ElementNodeData>[]; edges: Edge[] } {
  // DataFlow elements become edges, not nodes
  const nodeElements = elements.filter((e) => e.elementType !== "DataFlow");
  const dataFlowElements = elements.filter((e) => e.elementType === "DataFlow");

  // Build a name→id map for resolving DataFlow from/to
  const nameToId = new Map<string, string>();
  nodeElements.forEach((e) => nameToId.set(e.name.toLowerCase(), e.id));

  // Auto-layout with dagre
  const g = new dagre.graphlib.Graph();
  g.setDefaultEdgeLabel(() => ({}));
  g.setGraph({ rankdir: "LR", ranksep: 80, nodesep: 40 });

  nodeElements.forEach((e) => {
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

  const nodes: Node<ElementNodeData>[] = nodeElements.map((element) => {
    const nodeWithPos = g.node(element.id);
    const threatInfo = threatCountByElement?.get(element.id);

    return {
      id: element.id,
      type: "elementNode",
      position: {
        x: (nodeWithPos?.x ?? 0) - NODE_WIDTH / 2,
        y: (nodeWithPos?.y ?? 0) - NODE_HEIGHT / 2,
      },
      data: {
        element,
        threatCount: threatInfo?.count,
        maxSeverity: threatInfo?.maxSeverity ?? null,
      },
    };
  });

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
        style: { stroke: edgeThreatCount > 0 ? "#f59e0b" : "#94a3b8" },
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
