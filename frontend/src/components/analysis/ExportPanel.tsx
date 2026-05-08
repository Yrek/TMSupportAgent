import { Download, FileCode2, FileJson, FileText } from "lucide-react";
import { toast } from "sonner";
import { useExportAnalysis } from "@/api/threats";
import type { Threat } from "@/api/threats";
import type { ArchitectureModel } from "@/api/architecture";
import { Button } from "@/components/ui/button";

interface ExportPanelProps {
  orgId: string;
  jobId: string;
  analysisData: unknown;
  architecture?: ArchitectureModel | undefined;
  threats?: Threat[] | undefined;
}

export function ExportPanel({ orgId, jobId, analysisData, architecture, threats }: ExportPanelProps) {
  const exportAnalysis = useExportAnalysis(orgId, jobId);

  async function handleJsonExport() {
    try {
      await exportAnalysis.mutateAsync();
      toast.success("Download started");
    } catch {
      toast.error("Export failed");
    }
  }

  function handleMarkdownExport() {
    try {
      const md = renderAnalysisAsMarkdown(analysisData, threats);
      downloadTextFile(md, `threat-model-${jobId}.md`, "text/markdown");
      toast.success("Markdown downloaded");
    } catch {
      toast.error("Failed to generate Markdown");
    }
  }

  function handleMermaidExport() {
    try {
      const mermaid = renderArchitectureAsMermaid(architecture);
      downloadTextFile(mermaid, `architecture-${jobId}.mmd`, "text/plain");
      toast.success("Mermaid diagram downloaded");
    } catch {
      toast.error("Failed to generate Mermaid diagram");
    }
  }

  function handleTmBomExport() {
    try {
      const tmBom = renderAnalysisAsTmBom(analysisData, architecture, orgId, jobId, threats);
      downloadTextFile(JSON.stringify(tmBom, null, 2), `tm-bom-${jobId}.json`, "application/json");
      toast.success("TM-BOM downloaded");
    } catch {
      toast.error("Failed to generate TM-BOM");
    }
  }

  function handleThreatDragonV2Export() {
    try {
      const td = renderAnalysisAsThreatDragonV2(analysisData, architecture, orgId, jobId, threats);
      downloadTextFile(
        JSON.stringify(td, null, 2),
        `threat-dragon-v2-${jobId}.json`,
        "application/json",
      );
      toast.success("Threat Dragon v2 JSON downloaded");
    } catch {
      toast.error("Failed to generate Threat Dragon v2 JSON");
    }
  }

  return (
    <div className="space-y-6 p-4">
      <div>
        <h3 className="text-sm font-semibold">Export threat model</h3>
        <p className="mt-1 text-sm text-muted-foreground">
          Download the full analysis for offline review or integration with other tools.
        </p>
      </div>

      <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-5">
        <div className="rounded-lg border p-4 space-y-3">
          <div className="flex items-center gap-2">
            <FileJson className="h-5 w-5 text-primary" />
            <span className="font-medium">JSON export</span>
          </div>
          <p className="text-sm text-muted-foreground">
            Full structured analysis blob including all threats, mitigations, mappings, and metadata.
          </p>
          <Button
            onClick={handleJsonExport}
            disabled={exportAnalysis.isPending}
            className="w-full gap-2"
            variant="outline"
          >
            <Download className="h-4 w-4" />
            {exportAnalysis.isPending ? "Downloading..." : "Download JSON"}
          </Button>
        </div>

        <div className="rounded-lg border p-4 space-y-3">
          <div className="flex items-center gap-2">
            <FileText className="h-5 w-5 text-primary" />
            <span className="font-medium">Markdown report</span>
          </div>
          <p className="text-sm text-muted-foreground">
            Human-readable threat model report rendered from analysis data.
          </p>
          <Button onClick={handleMarkdownExport} className="w-full gap-2" variant="outline">
            <Download className="h-4 w-4" />
            Download Markdown
          </Button>
        </div>

        <div className="rounded-lg border p-4 space-y-3">
          <div className="flex items-center gap-2">
            <FileCode2 className="h-5 w-5 text-primary" />
            <span className="font-medium">Diagram as code</span>
          </div>
          <p className="text-sm text-muted-foreground">
            Export architecture as Mermaid (`.mmd`) so you can edit and version-control the diagram.
          </p>
          <Button onClick={handleMermaidExport} className="w-full gap-2" variant="outline">
            <Download className="h-4 w-4" />
            Download Mermaid
          </Button>
        </div>

        <div className="rounded-lg border p-4 space-y-3">
          <div className="flex items-center gap-2">
            <FileJson className="h-5 w-5 text-primary" />
            <span className="font-medium">TM-BOM</span>
          </div>
          <p className="text-sm text-muted-foreground">
            Portable threat-model BOM including architecture, methods, threats, and control mappings.
          </p>
          <Button onClick={handleTmBomExport} className="w-full gap-2" variant="outline">
            <Download className="h-4 w-4" />
            Download TM-BOM
          </Button>
        </div>

        <div className="rounded-lg border p-4 space-y-3">
          <div className="flex items-center gap-2">
            <FileJson className="h-5 w-5 text-primary" />
            <span className="font-medium">Threat Dragon v2</span>
          </div>
          <p className="text-sm text-muted-foreground">
            Best-effort Threat Dragon v2 style JSON projection of architecture and threats.
          </p>
          <Button onClick={handleThreatDragonV2Export} className="w-full gap-2" variant="outline">
            <Download className="h-4 w-4" />
            Download Threat Dragon v2
          </Button>
        </div>
      </div>
    </div>
  );
}

function downloadTextFile(content: string, fileName: string, mimeType: string) {
  const blob = new Blob([content], { type: mimeType });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = fileName;
  a.click();
  URL.revokeObjectURL(url);
}

function renderAnalysisAsMarkdown(data: unknown, dbThreats?: Threat[]): string {
  if (!data || typeof data !== "object") return "# Threat Model\n\nNo analysis data available.";
  const d = data as Record<string, unknown>;
  const statusByIdentifier = new Map<string, string>(
    (dbThreats ?? []).map((t) => [t.identifier, t.status]),
  );
  const confirmedThreats = asArray(d["confirmedThreats"]);
  const conditionalThreats = asArray(d["conditionalThreats"]);
  const userAddedThreats = (dbThreats ?? []).filter((t) => t.findingType === "UserAdded");
  const recommendations = asArray(d["secureDesignRecommendations"]);
  const remediation = asArray(d["prioritizedRemediationList"]);
  const reviewQuestions = asStringArray(d["reviewQuestions"]);
  const methods = asArray(d["selectedMethodsWithRationale"]);
  const classification = asStringArray(d["architectureClassification"]);

  const lines: string[] = [];
  lines.push("# Threat Model Analysis");
  lines.push("");

  if (typeof d["analysisStatus"] === "string") {
    lines.push(`Status: **${d["analysisStatus"]}**`);
    if (typeof d["partialReason"] === "string" && d["partialReason"].trim().length > 0) {
      lines.push(`Partial reason: ${d["partialReason"]}`);
    }
    lines.push("");
  }

  if (typeof d["systemSummary"] === "string") {
    lines.push("## System Summary");
    lines.push(d["systemSummary"]);
    lines.push("");
  }

  if (classification.length > 0) {
    lines.push("## Architecture Classification");
    classification.forEach((c) => lines.push(`- ${c}`));
    lines.push("");
  }

  if (methods.length > 0) {
    lines.push("## Selected Methods");
    methods.forEach((m) => {
      const method = asString((m as Record<string, unknown>)["method"]);
      const rationale = asString((m as Record<string, unknown>)["rationale"]);
      if (method) lines.push(`- **${method}**${rationale ? `: ${rationale}` : ""}`);
    });
    lines.push("");
  }

  appendThreatSection(lines, "Confirmed Threats", confirmedThreats, statusByIdentifier);
  appendThreatSection(lines, "Conditional Threats", conditionalThreats, statusByIdentifier);

  if (userAddedThreats.length > 0) {
    lines.push(`## User-Added Threats (${userAddedThreats.length})`);
    userAddedThreats.forEach((t) => {
      lines.push(`### ${t.identifier || t.id} - ${t.title}`);
      const status = t.status;
      lines.push(`Status: ${status} | Category: ${t.methodCategory}`);
      if (t.description) lines.push(`- Description: ${t.description}`);
      if (t.attackScenario) lines.push(`- Attack scenario: ${t.attackScenario}`);
      if (t.mitigations.length > 0) {
        lines.push("- Mitigations:");
        t.mitigations.forEach((m) => {
          lines.push(`  - ${m.title} (${m.priority}): ${m.description}`);
          m.acceptanceCriteria.forEach((ac) => lines.push(`      - ${ac}`));
        });
      }
      lines.push("");
    });
  }

  if (recommendations.length > 0) {
    lines.push("## Secure Design Recommendations");
    recommendations.forEach((r) => {
      const rec = r as Record<string, unknown>;
      const title = asString(rec["title"]) ?? "Recommendation";
      const description = asString(rec["description"]) ?? "";
      const principles = asStringArray(rec["principles"]);
      const affected = asStringArray(rec["affectedElementLabels"]);
      lines.push(`### ${title}`);
      if (description) lines.push(description);
      if (principles.length > 0) lines.push(`- Principles: ${principles.join(", ")}`);
      if (affected.length > 0) lines.push(`- Affected elements: ${affected.join(", ")}`);
      lines.push("");
    });
  }

  if (remediation.length > 0) {
    lines.push("## Prioritized Remediation");
    remediation.forEach((r) => {
      const item = r as Record<string, unknown>;
      const id = asString(item["threatIdentifier"]) ?? "N/A";
      const title = asString(item["title"]) ?? "Remediation";
      const priority = asString(item["priority"]) ?? "unknown";
      const summary = asString(item["mitigationSummary"]) ?? "";
      lines.push(`- **${id} - ${title}** (${priority})`);
      if (summary) lines.push(`  - ${summary}`);
    });
    lines.push("");
  }

  if (reviewQuestions.length > 0) {
    lines.push("## Questions Requiring Review");
    reviewQuestions.forEach((q) => lines.push(`- ${q}`));
    lines.push("");
  }

  lines.push("*Generated by Threat Modeling Agent*");
  return lines.join("\n");
}

function appendThreatSection(lines: string[], title: string, threats: unknown[], statusByIdentifier?: Map<string, string>) {
  if (threats.length === 0) return;
  lines.push(`## ${title} (${threats.length})`);
  threats.forEach((t) => {
    const threat = t as Record<string, unknown>;
    const id = asString(threat["identifier"]) ?? "T-???";
    const threatTitle = asString(threat["title"]) ?? "Untitled threat";
    const status = statusByIdentifier?.get(id);
    const methodCategory = asString(threat["methodCategory"]);
    const sourceMethods = asStringArray(threat["sourceMethods"]);
    const confidence = asString(threat["confidence"]);
    const findingType = asString(threat["findingType"]);
    const affected = asStringArray(threat["affectedElementLabels"]);
    const description = asString(threat["description"]);
    const attackScenario = asString(threat["attackScenario"]);
    const securityImpact = asString(threat["securityImpact"]);
    const privacyImpact = asString(threat["privacyImpact"]);
    const controlGaps = asString(threat["controlGaps"]);
    const mitigations = asArray(threat["mitigations"]);
    const frameworks = asArray(threat["frameworkMappings"]);

    lines.push(`### ${id} - ${threatTitle}`);
    const riskRating = isRecord(threat["riskRating"]) ? threat["riskRating"] as Record<string, unknown> : null;
    const meta: string[] = [];
    if (riskRating) {
      const sev = asString(riskRating["severity"]);
      const lkl = asString(riskRating["likelihood"]);
      const imp = asString(riskRating["impact"]);
      if (sev || lkl || imp)
        meta.push(`Risk: ${sev ? sev.toUpperCase() : "?"} (likelihood: ${lkl ?? "?"}, impact: ${imp ?? "?"})`);
    }
    if (methodCategory) meta.push(`Category: ${methodCategory}`);
    if (sourceMethods.length > 0) meta.push(`Methods: ${sourceMethods.join(", ")}`);
    if (confidence) meta.push(`Confidence: ${confidence}`);
    if (findingType) meta.push(`Type: ${findingType}`);
    if (status) meta.push(`Status: ${status}`);
    if (meta.length > 0) lines.push(meta.join(" | "));
    if (affected.length > 0) lines.push(`Affected elements: ${affected.join(", ")}`);
    if (description) lines.push(`- Description: ${description}`);
    if (attackScenario) lines.push(`- Attack scenario: ${attackScenario}`);
    const preconditions = asString(threat["preconditions"]);
    const impactedAssets = asStringArray(threat["impactedAssets"]);
    const existingControls = asString(threat["existingControls"]);
    const evidenceBasis = asStringArray(threat["evidenceBasis"]);
    const evidenceStrength = asString(threat["evidenceStrength"]);
    const assumptions = asString(threat["assumptions"]);
    if (preconditions) lines.push(`- Preconditions: ${preconditions}`);
    if (impactedAssets.length > 0) lines.push(`- Impacted assets: ${impactedAssets.join(", ")}`);
    if (securityImpact) lines.push(`- Security impact: ${securityImpact}`);
    if (privacyImpact) lines.push(`- Privacy impact: ${privacyImpact}`);
    if (existingControls) lines.push(`- Existing controls: ${existingControls}`);
    if (controlGaps) lines.push(`- Control gaps: ${controlGaps}`);
    if (evidenceStrength || evidenceBasis.length > 0)
      lines.push(`- Evidence: ${evidenceStrength ?? ""}${evidenceBasis.length > 0 ? ` — ${evidenceBasis.join(", ")}` : ""}`);
    if (assumptions) lines.push(`- Assumptions: ${assumptions}`);

    if (mitigations.length > 0) {
      lines.push("- Mitigations:");
      mitigations.forEach((m) => {
        const mitigation = m as Record<string, unknown>;
        const mTitle = asString(mitigation["title"]) ?? "Mitigation";
        const mPriority = asString(mitigation["priority"]);
        const mDescription = asString(mitigation["description"]);
        const mCriteria = asStringArray(mitigation["acceptanceCriteria"]);
        lines.push(
          `  - ${mTitle}${mPriority ? ` (${mPriority})` : ""}${mDescription ? `: ${mDescription}` : ""}`,
        );
        if (mCriteria.length > 0) {
          lines.push(`    - Done when:`);
          mCriteria.forEach((ac) => lines.push(`      - ${ac}`));
        }
      });
    }

    if (frameworks.length > 0) {
      lines.push("- Framework mappings:");
      frameworks.forEach((f) => {
        const mapping = f as Record<string, unknown>;
        const framework = asString(mapping["framework"]) ?? "framework";
        const reference = asString(mapping["reference"]) ?? "reference";
        lines.push(`  - ${framework}: ${reference}`);
      });
    }
    lines.push("");
  });
}

function renderArchitectureAsMermaid(architecture?: ArchitectureModel): string {
  if (!architecture) return "flowchart LR\n  %% No architecture available";

  const nodeElements = architecture.elements.filter((e) => e.elementType !== "DataFlow");
  const nodeIds = new Map<string, string>();
  nodeElements.forEach((el, idx) => nodeIds.set(el.id, `N${idx + 1}`));

  const lines: string[] = ["flowchart LR"];

  nodeElements.forEach((el) => {
    const nodeId = nodeIds.get(el.id);
    if (!nodeId) return;
    const label = mermaidEscape(el.name);
    const shape = mermaidShape(el.elementType, label);
    lines.push(`  ${nodeId}${shape}`);
  });

  const labeledNameToId = new Map<string, string>();
  nodeElements.forEach((el) => {
    const nodeId = nodeIds.get(el.id);
    if (nodeId) labeledNameToId.set(el.name.trim().toLowerCase(), nodeId);
  });

  architecture.elements
    .filter((e) => e.elementType === "DataFlow")
    .forEach((flow) => {
      const fromName = asString(flow.properties?.["from"]) ?? tryParseFlowName(flow.name).from;
      const toName = asString(flow.properties?.["to"]) ?? tryParseFlowName(flow.name).to;
      if (!fromName || !toName) return;

      const fromId = labeledNameToId.get(fromName.trim().toLowerCase());
      const toId = labeledNameToId.get(toName.trim().toLowerCase());
      if (!fromId || !toId) return;

      const label = flow.description?.trim();
      if (label) {
        lines.push(`  ${fromId} -->|${mermaidEdgeEscape(label)}| ${toId}`);
      } else {
        lines.push(`  ${fromId} --> ${toId}`);
      }
    });

  return lines.join("\n");
}

function renderAnalysisAsTmBom(
  data: unknown,
  architecture: ArchitectureModel | undefined,
  orgId: string,
  jobId: string,
  dbThreats?: Threat[],
) {
  const d = isRecord(data) ? data : {};
  const statusByIdentifier = new Map<string, string>(
    (dbThreats ?? []).map((t) => [t.identifier, t.status]),
  );
  const userAddedThreats = (dbThreats ?? []).filter((t) => t.findingType === "UserAdded").map(normalizeDbThreat);
  const threats = [...extractThreatsFromAnalysis(data, statusByIdentifier), ...userAddedThreats];
  const methods = asArray(d["selectedMethodsWithRationale"]);

  const nodeElements = architecture?.elements.filter((e) => e.elementType !== "DataFlow") ?? [];
  const flowElements = architecture?.elements.filter((e) => e.elementType === "DataFlow") ?? [];

  return {
    bomFormat: "TM-BOM",
    specVersion: "1.0",
    serialNumber: `urn:uuid:${jobId}`,
    version: 1,
    metadata: {
      generatedAt: new Date().toISOString(),
      tool: {
        vendor: "ThreatModelingAgent",
        name: "Threat Modeling Agent",
        version: "v2",
      },
      organizationId: orgId,
      jobId,
    },
    system: {
      summary: asString(d["systemSummary"]),
      analysisStatus: asString(d["analysisStatus"]),
      partialReason: asString(d["partialReason"]),
      architectureClassification: asStringArray(d["architectureClassification"]),
      reviewQuestions: asStringArray(d["reviewQuestions"]),
    },
    methods: methods
      .map((m) => {
        const rec = isRecord(m) ? m : {};
        return {
          method: asString(rec["method"]),
          rationale: asString(rec["rationale"]),
          requiredBySpec: rec["requiredBySpec"] === true,
        };
      })
      .filter((m) => m.method),
    architecture: {
      modelId: architecture?.id ?? null,
      version: architecture?.version ?? null,
      systemPurpose: architecture?.systemPurpose ?? null,
      elements: nodeElements.map((e) => ({
        id: e.id,
        name: e.name,
        elementType: e.elementType,
        description: e.description,
        source: e.source,
        extractionConfidence: e.extractionConfidence,
        properties: e.properties ?? {},
      })),
      dataFlows: flowElements.map((f) => ({
        id: f.id,
        name: f.name,
        description: f.description,
        from: asString(f.properties?.["from"]),
        to: asString(f.properties?.["to"]),
        source: f.source,
      })),
    },
    threats: threats.map((t) => ({
      id: t.id,
      identifier: t.identifier,
      title: t.title,
      findingType: t.findingType,
      methodCategory: t.methodCategory,
      sourceMethods: t.sourceMethods,
      confidence: t.confidence,
      riskRating: t.riskRating,
      affectedElementLabels: t.affectedElementLabels,
      description: t.description,
      attackScenario: t.attackScenario,
      preconditions: t.preconditions,
      impactedAssets: t.impactedAssets,
      securityImpact: t.securityImpact,
      privacyImpact: t.privacyImpact,
      existingControls: t.existingControls,
      controlGaps: t.controlGaps,
      evidenceBasis: t.evidenceBasis,
      evidenceStrength: t.evidenceStrength,
      assumptions: t.assumptions,
      mitigations: t.mitigations,
      frameworkMappings: t.frameworkMappings,
      disposition: t.disposition,
      status: t.status,
    })),
    secureDesignRecommendations: asArray(d["secureDesignRecommendations"]),
    prioritizedRemediationList: asArray(d["prioritizedRemediationList"]),
  };
}

function renderAnalysisAsThreatDragonV2(
  data: unknown,
  architecture: ArchitectureModel | undefined,
  orgId: string,
  jobId: string,
  dbThreats?: Threat[],
) {
  const d = isRecord(data) ? data : {};

  // Build status lookup from DB threats (identifier → status)
  const statusByIdentifier = new Map<string, string>(
    (dbThreats ?? []).map((t) => [t.identifier, t.status]),
  );

  // Blob extraction gives rich fields (attackScenario, evidenceBasis, etc.)
  const blobThreats = extractThreatsFromAnalysis(data, statusByIdentifier);

  // Add user-added threats from DB (they are never in the blob)
  const userAddedThreats = (dbThreats ?? [])
    .filter((t) => t.findingType === "UserAdded")
    .map(normalizeDbThreat);

  const threats = [...blobThreats, ...userAddedThreats];
  const nodeElements = architecture?.elements.filter((e) => e.elementType !== "DataFlow") ?? [];
  const flowElements = architecture?.elements.filter((e) => e.elementType === "DataFlow") ?? [];

  const nameToNodeId = new Map<string, string>();
  nodeElements.forEach((e) => {
    nameToNodeId.set(e.name.trim().toLowerCase(), e.id);
  });

  const diagramCells = [
    ...nodeElements.map((e) => ({
      id: e.id,
      type: mapToThreatDragonType(e.elementType),
      name: e.name,
      description: e.description ?? "",
      outOfScope: false,
      properties: e.properties ?? {},
    })),
    ...flowElements.map((f) => {
      const parsed = tryParseFlowName(f.name);
      const from = asString(f.properties?.["from"]) ?? parsed.from;
      const to = asString(f.properties?.["to"]) ?? parsed.to;
      return {
        id: f.id,
        type: "tm.Flow",
        name: f.name,
        description: f.description ?? "",
        sourceId: from ? nameToNodeId.get(from.trim().toLowerCase()) ?? null : null,
        targetId: to ? nameToNodeId.get(to.trim().toLowerCase()) ?? null : null,
        outOfScope: false,
        properties: f.properties ?? {},
      };
    }),
  ];

  return {
    version: "2.1.0",
    summary: {
      title: asString(d["systemSummary"]) ?? `Threat Model ${jobId}`,
      owner: orgId,
      description: "Generated by Threat Modeling Agent",
      id: 0,
    },
    detail: {
      contributors: [],
      reviewer: "",
      diagrams: [
        {
          title: "Architecture",
          id: 0,
          version: "2.1.0",
          diagramType: "STRIDE",
          cells: diagramCells,
        },
      ],
      threats: threats.map((t, idx) => ({
        id: idx + 1,
        title: t.title,
        status: mapToThreatDragonStatus(t.status),
        severity: t.riskRating?.severity ?? t.confidence,
        type: t.methodCategory,
        description: [
          t.description,
          t.attackScenario ? `Attack scenario: ${t.attackScenario}` : null,
          t.disposition === "conditional" ? `Conditional finding` : null,
        ].filter(Boolean).join("\n\n"),
        mitigation: t.mitigations
          .map((m) => `${m.title}${m.acceptanceCriteria.length > 0 ? ` (done when: ${m.acceptanceCriteria.join("; ")})` : ""}`)
          .join("; "),
        references: t.frameworkMappings.map((f) => `${f.framework}:${f.reference}`),
        sourceMethods: t.sourceMethods,
        affectedElementLabels: t.affectedElementLabels,
      })),
      threatTop: threats.length,
    },
  };
}

function mermaidShape(type: string, label: string): string {
  switch (type) {
    case "Actor":
      return `([${label}])`;
    case "DataStore":
      return `[(${label})]`;
    case "ExternalSystem":
      return `[[${label}]]`;
    case "TrustBoundary":
      return `{{${label}}}`;
    default:
      return `[${label}]`;
  }
}

function tryParseFlowName(name: string): { from: string | null; to: string | null } {
  const arrow = name.includes("->") ? "->" : null;
  if (!arrow) return { from: null, to: null };
  const [from, to] = name.split(arrow).map((x) => x.trim());
  if (!from || !to) return { from: null, to: null };
  return { from, to };
}

function mermaidEscape(input: string): string {
  return input.replaceAll('"', "\\\"");
}

function mermaidEdgeEscape(input: string): string {
  return input.replaceAll("|", "/").replaceAll("\n", " ").trim();
}

function asArray(value: unknown): unknown[] {
  return Array.isArray(value) ? value : [];
}

function asStringArray(value: unknown): string[] {
  if (!Array.isArray(value)) return [];
  return value.filter((v): v is string => typeof v === "string" && v.trim().length > 0);
}

function asString(value: unknown): string | null {
  return typeof value === "string" && value.trim().length > 0 ? value : null;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value !== null && typeof value === "object";
}

function mapToThreatDragonType(elementType: string): string {
  switch (elementType) {
    case "Actor":
      return "tm.Actor";
    case "DataStore":
      return "tm.Store";
    case "TrustBoundary":
      return "tm.Boundary";
    default:
      return "tm.Process";
  }
}

type NormalizedThreat = {
  id: string;
  identifier: string;
  title: string;
  findingType: string;
  methodCategory: string | null;
  sourceMethods: string[];
  confidence: string | null;
  affectedElementLabels: string[];
  description: string | null;
  attackScenario: string | null;
  preconditions: string | null;
  impactedAssets: string[];
  securityImpact: string | null;
  privacyImpact: string | null;
  existingControls: string | null;
  controlGaps: string | null;
  evidenceBasis: string[];
  evidenceStrength: string | null;
  assumptions: string | null;
  riskRating: { likelihood: string | null; impact: string | null; severity: string | null; likelihoodJustification: string | null; impactJustification: string | null } | null;
  mitigations: Array<{ title: string; description: string | null; priority: string | null; acceptanceCriteria: string[] }>;
  frameworkMappings: Array<{ framework: string; reference: string; mappingType: string | null }>;
  disposition: "confirmed" | "conditional" | "user_added";
  status: string;
};

function extractThreatsFromAnalysis(data: unknown, statusByIdentifier?: Map<string, string>): NormalizedThreat[] {
  if (!isRecord(data)) return [];
  const confirmed = asArray(data["confirmedThreats"]).map((t) => normalizeThreat(t, "confirmed", statusByIdentifier));
  const conditional = asArray(data["conditionalThreats"]).map((t) => normalizeThreat(t, "conditional", statusByIdentifier));
  return [...confirmed, ...conditional];
}

function normalizeThreat(value: unknown, disposition: "confirmed" | "conditional" | "user_added", statusByIdentifier?: Map<string, string>): NormalizedThreat {
  const t = isRecord(value) ? value : {};
  const identifier = asString(t["identifier"]) ?? "";
  return {
    id: asString(t["id"]) ?? "",
    identifier,
    title: asString(t["title"]) ?? "Untitled threat",
    findingType: asString(t["findingType"]) ?? "",
    methodCategory: asString(t["methodCategory"]),
    sourceMethods: asStringArray(t["sourceMethods"]),
    confidence: asString(t["confidence"]),
    affectedElementLabels: asStringArray(t["affectedElementLabels"]),
    description: asString(t["description"]),
    attackScenario: asString(t["attackScenario"]),
    preconditions: asString(t["preconditions"]),
    impactedAssets: asStringArray(t["impactedAssets"]),
    securityImpact: asString(t["securityImpact"]),
    privacyImpact: asString(t["privacyImpact"]),
    existingControls: asString(t["existingControls"]),
    controlGaps: asString(t["controlGaps"]),
    evidenceBasis: asStringArray(t["evidenceBasis"]),
    evidenceStrength: asString(t["evidenceStrength"]),
    assumptions: asString(t["assumptions"]),
    riskRating: (() => {
      const rr = t["riskRating"];
      if (!isRecord(rr)) return null;
      return {
        likelihood: asString(rr["likelihood"]),
        impact: asString(rr["impact"]),
        severity: asString(rr["severity"]),
        likelihoodJustification: asString(rr["likelihoodJustification"]),
        impactJustification: asString(rr["impactJustification"]),
      };
    })(),
    mitigations: asArray(t["mitigations"]).map((m) => {
      const rec = isRecord(m) ? m : {};
      return {
        title: asString(rec["title"]) ?? "Mitigation",
        description: asString(rec["description"]),
        priority: asString(rec["priority"]),
        acceptanceCriteria: asStringArray(rec["acceptanceCriteria"]),
      };
    }),
    frameworkMappings: asArray(t["frameworkMappings"]).map((f) => {
      const rec = isRecord(f) ? f : {};
      return {
        framework: asString(rec["framework"]) ?? "unknown",
        reference: asString(rec["reference"]) ?? "unknown",
        mappingType: asString(rec["mappingType"]),
      };
    }),
    disposition,
    status: statusByIdentifier?.get(identifier) ?? "Open",
  };
}

function mapToThreatDragonStatus(status: string): string {
  switch (status) {
    case "Mitigated": return "Mitigated";
    case "Accepted":
    case "Rejected":  return "Not Applicable";
    default:          return "Open";
  }
}

function normalizeDbThreat(t: Threat): NormalizedThreat {
  return {
    id: t.id,
    identifier: t.identifier,
    title: t.title,
    findingType: t.findingType,
    methodCategory: t.methodCategory,
    sourceMethods: t.sourceMethods ?? [],
    confidence: t.confidence,
    affectedElementLabels: [],
    description: t.description,
    attackScenario: t.attackScenario,
    preconditions: t.preconditions ?? null,
    impactedAssets: t.impactedAssets ?? [],
    securityImpact: t.securityImpact ?? null,
    privacyImpact: t.privacyImpact ?? null,
    existingControls: t.existingControls ?? null,
    controlGaps: t.controlGaps ?? null,
    evidenceBasis: [],
    evidenceStrength: null,
    assumptions: null,
    riskRating: t.riskRating
      ? {
          likelihood: t.riskRating.likelihood,
          impact: t.riskRating.impact,
          severity: t.riskRating.severity,
          likelihoodJustification: t.riskRating.likelihoodJustification,
          impactJustification: t.riskRating.impactJustification,
        }
      : null,
    mitigations: t.mitigations.map((m) => ({
      title: m.title,
      description: m.description,
      priority: m.priority,
      acceptanceCriteria: m.acceptanceCriteria,
    })),
    frameworkMappings: t.frameworkMappings.map((f) => ({
      framework: f.framework,
      reference: f.reference,
      mappingType: f.mappingType,
    })),
    disposition: t.findingType === "UserAdded" ? "user_added" : t.findingType === "Conditional" ? "conditional" : "confirmed",
    status: t.status,
  };
}
