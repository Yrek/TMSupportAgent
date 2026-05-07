import { useEffect, useState } from "react";
import { useParams, useNavigate, Link } from "react-router-dom";
import { usePageTitle } from "@/hooks/usePageTitle";
import { toast } from "sonner";
import { PlusCircle, ArrowLeft, Pencil } from "lucide-react";
import {
  useArchitecture,
  useAddElement,
  usePatchElement,
  useDeleteElement,
  useCorrectElement,
  useConfirmArchitecture,
  useUpdateDeploymentContext,
} from "@/api/architecture";
import { useJob } from "@/api/jobs";
import { useAddThreat } from "@/api/threats";
import { AddThreatModal } from "@/components/threats/AddThreatModal";
import { AppShell } from "@/components/layout/AppShell";
import { JobStatusBadge } from "@/components/jobs/JobStatusBadge";
import { ArchCanvas } from "@/components/architecture/ArchCanvas";
import { ElementListPanel } from "@/components/architecture/ElementListPanel";
import { ElementDetailPanel } from "@/components/architecture/ElementDetailPanel";
import { AddElementModal } from "@/components/architecture/AddElementModal";
import { ArchitectureMetaPanel } from "@/components/architecture/ArchitectureMetaPanel";
import { ConfirmDialog } from "@/components/common/ConfirmDialog";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { Textarea } from "@/components/ui/textarea";
import { Label } from "@/components/ui/label";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from "@/components/ui/dialog";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import type { ArchitectureElement, ArchitectureModel, DeploymentContext } from "@/api/architecture";
import { requiredParam } from "@/lib/requiredParam";

const METHOD_OPTIONS = [
  { value: "stride",                     label: "STRIDE" },
  { value: "abuse_case",                 label: "Abuse Cases" },
  { value: "linddun",                    label: "LINDDUN (Privacy)" },
  { value: "mitre_attack",              label: "MITRE ATT&CK" },
  { value: "owasp_cumulus",             label: "OWASP Cumulus (Cloud)" },
  { value: "owasp_cornucopia",          label: "OWASP Cornucopia" },
  { value: "tenant_isolation",          label: "Tenant Isolation" },
  { value: "identity_session_delegation", label: "Identity & Session Trust" },
  { value: "ai_llm_threat",            label: "AI/LLM Threats" },
  { value: "maestro",                   label: "MAESTRO (AI/ML)" },
  { value: "supply_chain",              label: "Supply Chain" },
  { value: "availability_resilience",   label: "Availability & Resilience" },
  { value: "vast",                      label: "VAST" },
  { value: "pasta",                     label: "PASTA" },
  { value: "octave",                    label: "OCTAVE (Advanced)" },
  { value: "trike",                     label: "TRIKE (Advanced)" },
] as const;

function computeSuggestedMethods(arch: ArchitectureModel): string[] {
  const suggested = new Set<string>(["stride", "abuse_case"]);

  const hasLlmBoundary = arch.elements.some((e) => e.elementType === "LlmBoundary");
  const hasMultiTenant =
    arch.elements.some((e) => e.name.toLowerCase().includes("tenant")) ||
    arch.classification.some((c) => c === "multi_tenant_saas");
  const hasPrivacy =
    arch.elements.some(
      (e) =>
        e.name.toLowerCase().includes("pii") ||
        e.name.toLowerCase().includes("personal") ||
        e.name.toLowerCase().includes("privacy"),
    ) || arch.classification.some((c) => c === "privacy_heavy");
  const hasCloud =
    arch.deploymentContext != null &&
    arch.deploymentContext.environment !== "unknown" &&
    arch.deploymentContext.environment !== "on_prem";
  const externalCount = arch.elements.filter((e) => e.elementType === "ExternalSystem").length;

  if (hasLlmBoundary) {
    suggested.add("ai_llm_threat");
    suggested.add("maestro");
  }
  if (hasMultiTenant) suggested.add("tenant_isolation");
  if (hasPrivacy) suggested.add("linddun");
  if (hasCloud) suggested.add("owasp_cumulus");
  if (externalCount > 2) suggested.add("supply_chain");

  return [...suggested];
}

function DeploymentContextBadge({ ctx }: { ctx: DeploymentContext }) {
  const envLabel: Record<string, string> = {
    aws: "AWS",
    azure: "Azure",
    gcp: "GCP",
    on_prem: "On-Prem",
    hybrid: "Hybrid",
    unknown: "Unknown",
  };
  const controlLabel: Record<string, string> = {
    waf: "WAF",
    cdn: "CDN",
    api_gateway: "API Gateway",
    load_balancer: "Load Balancer",
    ddos_protection: "DDoS Protection",
  };

  return (
    <div className="flex flex-wrap items-center gap-2 text-xs">
      <span className="rounded bg-primary/10 px-2 py-0.5 font-medium text-primary">
        {envLabel[ctx.environment] ?? ctx.environment}
      </span>
      {ctx.containerized && (
        <span className="rounded bg-muted px-2 py-0.5 text-muted-foreground">Containerized</span>
      )}
      {ctx.serverless && (
        <span className="rounded bg-muted px-2 py-0.5 text-muted-foreground">Serverless</span>
      )}
      {ctx.infraControls.map((c) => (
        <span key={c} className="rounded bg-muted px-2 py-0.5 text-muted-foreground">
          {controlLabel[c] ?? c}
        </span>
      ))}
    </div>
  );
}

export function ReviewPage() {
  const params = useParams<{ orgId: string; jobId: string }>();
  const orgId = requiredParam(params.orgId, "orgId");
  const jobId = requiredParam(params.jobId, "jobId");
  const navigate = useNavigate();

  const { data: job, isLoading: jobLoading } = useJob(orgId, jobId);
  const { data: architecture, isLoading: archLoading } = useArchitecture(orgId, jobId);

  const addElement = useAddElement(orgId, jobId);
  const patchElement = usePatchElement(orgId, jobId);
  const deleteElement = useDeleteElement(orgId, jobId);
  const correctElement = useCorrectElement(orgId, jobId);
  const confirmArch = useConfirmArchitecture(orgId, jobId);
  const updateDeploymentContext = useUpdateDeploymentContext(orgId, jobId);
  // GAP-TH7: pre-analysis threat/concern addition during AwaitingReview
  const addThreat = useAddThreat(orgId, jobId);

  usePageTitle(job ? `${job.title} — Review` : "Architecture Review");

  const [selectedElement, setSelectedElement] = useState<ArchitectureElement | null>(null);
  const [showAddModal, setShowAddModal] = useState(false);
  const [showConfirmDialog, setShowConfirmDialog] = useState(false);
  const [confirmNote, setConfirmNote] = useState("");
  const [selectedMethods, setSelectedMethods] = useState<string[]>([]);
  const [keyboardDeleteElement, setKeyboardDeleteElement] = useState<ArchitectureElement | null>(null);
  const [drawFlowMode, setDrawFlowMode] = useState(false);
  const [showRemovedElements, setShowRemovedElements] = useState(false);
  const [descExpanded, setDescExpanded] = useState(false);
  // GAP-TH7: pre-analysis concern modal
  const [showAddThreatModal, setShowAddThreatModal] = useState(false);
  const [showDeploymentEdit, setShowDeploymentEdit] = useState(false);
  const [deploymentEditForm, setDeploymentEditForm] = useState<{
    environment: string;
    containerized: boolean;
    serverless: boolean;
    infraControls: string[];
  }>({ environment: "unknown", containerized: false, serverless: false, infraControls: [] });

  const isReadOnly = job?.status !== "AwaitingReview";
  const allElements = architecture?.elements ?? [];
  const activeElements = allElements.filter((e) => {
    if (e.source !== "Extracted") return true;
    return !e.corrections.some((c) => c.correctionType === "MarkIncorrect");
  });
  const elements = showRemovedElements ? allElements : activeElements;
  const nonDataFlow = activeElements.filter((e) => e.elementType !== "DataFlow");
  const canConfirm = !isReadOnly && nonDataFlow.length > 0;

  useEffect(() => {
    if (!selectedElement) return;
    const stillVisible = elements.some((e) => e.id === selectedElement.id);
    if (!stillVisible) setSelectedElement(null);
  }, [elements, selectedElement]);

  // Pre-select methods based on detected architecture features (runs once when architecture loads)
  useEffect(() => {
    if (architecture && selectedMethods.length === 0) {
      setSelectedMethods(computeSuggestedMethods(architecture));
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [architecture?.id]);

  async function handleCreateDataFlow(from: ArchitectureElement, to: ArchitectureElement) {
    const exists = elements.some(
      (e) =>
        e.elementType === "DataFlow" &&
        String(e.properties?.["from"] ?? "").toLowerCase() === from.name.toLowerCase() &&
        String(e.properties?.["to"] ?? "").toLowerCase() === to.name.toLowerCase(),
    );

    if (exists) {
      toast.info("A flow between these elements already exists.");
      return;
    }

    await addElement.mutateAsync({
      elementType: "DataFlow",
      name: `${from.name} -> ${to.name}`,
      properties: {
        from: from.name,
        to: to.name,
      },
    });

    toast.success("Data flow added");
  }

  async function handleConfirm() {
    if (selectedMethods.length === 0) {
      toast.error("Select at least one method/framework before confirming.");
      return;
    }

    try {
      await confirmArch.mutateAsync({
        note: confirmNote || undefined,
        selectedMethods,
      });
      toast.success("Analysis started");
      navigate(`/orgs/${orgId}/jobs/${jobId}`);
    } catch {
      toast.error("Failed to confirm architecture");
    } finally {
      setShowConfirmDialog(false);
    }
  }

  const isLoading = jobLoading || archLoading;

  if (isLoading) {
    return (
      <AppShell>
        <div className="flex h-[calc(100vh-48px)] flex-col">
          <div className="flex items-center gap-4 border-b p-4">
            <Skeleton className="h-7 w-48" />
            <Skeleton className="h-6 w-24" />
          </div>
          <div className="flex flex-1 gap-0 overflow-hidden">
            <Skeleton className="h-full w-56 rounded-none" />
            <div className="flex-1 p-4">
              <Skeleton className="h-full w-full" />
            </div>
          </div>
        </div>
      </AppShell>
    );
  }

  return (
    <AppShell>
      <div className="flex h-[calc(100vh-48px)] flex-col">
        {/* Top bar */}
        <div className="flex items-center gap-3 border-b px-4 py-3 shrink-0">
          <Link
            to={`/orgs/${orgId}/jobs/${jobId}`}
            className="text-muted-foreground hover:text-foreground transition-colors"
            aria-label="Back to job"
          >
            <ArrowLeft className="h-5 w-5" />
          </Link>
          <div className="flex-1 min-w-0">
            <h1 className="truncate font-semibold">{job?.title ?? "Architecture review"}</h1>
          </div>
          {job && <JobStatusBadge status={job.status} />}

          {isReadOnly && (
            <span className="text-sm text-muted-foreground">Read-only — {job?.status}</span>
          )}

          {/* GAP-TH7: add pre-analysis threat while architecture is still under review */}
          {!isReadOnly && nonDataFlow.length > 0 && (
            <Button
              variant="outline"
              size="sm"
              onClick={() => setShowAddThreatModal(true)}
              className="gap-1.5"
            >
              Add threat
            </Button>
          )}

          {!isReadOnly && (
            <Button
              variant={drawFlowMode ? "default" : "outline"}
              size="sm"
              onClick={() => setDrawFlowMode((v) => !v)}
            >
              {drawFlowMode ? "Draw flow: ON" : "Draw flow: OFF"}
            </Button>
          )}

          <Button
            variant={showRemovedElements ? "default" : "outline"}
            size="sm"
            onClick={() => setShowRemovedElements((v) => !v)}
          >
            {showRemovedElements ? "Show removed: ON" : "Show removed: OFF"}
          </Button>

          {!isReadOnly && (
            <Button
              onClick={() => setShowConfirmDialog(true)}
              disabled={!canConfirm}
              title={!canConfirm ? "Add at least one element before confirming" : undefined}
            >
              Confirm architecture
            </Button>
          )}
        </div>

        {/* Architecture metadata */}
        {architecture && <ArchitectureMetaPanel architecture={architecture} />}

        {/* Deployment context — auto-detected from diagram, editable before analysis */}
        {architecture?.deploymentContext && (
          <div className="border-b px-4 py-2 shrink-0">
            <div className="flex items-center gap-3">
              <span className="text-xs font-semibold uppercase tracking-wide text-muted-foreground shrink-0">
                Deployment
              </span>
              <DeploymentContextBadge ctx={architecture.deploymentContext} />
              {!isReadOnly && (
                <Button
                  variant="ghost"
                  size="sm"
                  className="h-6 px-2 ml-auto"
                  onClick={() => {
                    setDeploymentEditForm({
                      environment: architecture.deploymentContext!.environment,
                      containerized: architecture.deploymentContext!.containerized,
                      serverless: architecture.deploymentContext!.serverless,
                      infraControls: [...architecture.deploymentContext!.infraControls],
                    });
                    setShowDeploymentEdit(true);
                  }}
                >
                  <Pencil className="h-3 w-3 mr-1" />
                  <span className="text-xs">Edit</span>
                </Button>
              )}
              {isReadOnly && (
                <span className="text-xs text-muted-foreground ml-auto">Auto-detected</span>
              )}
            </div>
          </div>
        )}

        {(job?.applicationDescription || job?.architectureDescription) && (() => {
          const COLLAPSE_CHARS = 300;
          const archDesc = job.architectureDescription ?? "";
          const isLong = archDesc.length > COLLAPSE_CHARS;
          const displayedArch = !descExpanded && isLong
            ? archDesc.slice(0, COLLAPSE_CHARS) + "…"
            : archDesc;
          return (
            <div className="border-b px-4 py-2 shrink-0">
              <div className="rounded-md border bg-muted/30 px-3 py-2 space-y-2">
                {job.applicationDescription && (
                  <div>
                    <p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">Application Description</p>
                    <p className="text-sm whitespace-pre-wrap">{job.applicationDescription}</p>
                  </div>
                )}
                {archDesc && (
                  <div>
                    <p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">Architecture Description</p>
                    <p className="text-sm whitespace-pre-wrap">{displayedArch}</p>
                    {isLong && (
                      <button
                        onClick={() => setDescExpanded((v) => !v)}
                        className="mt-1 text-xs text-muted-foreground hover:text-foreground underline-offset-2 hover:underline"
                      >
                        {descExpanded ? "Show less" : "Show more"}
                      </button>
                    )}
                  </div>
                )}
              </div>
            </div>
          );
        })()}

        {/* Three-panel layout */}
        <div className="flex flex-1 overflow-hidden">
          {/* Left: element list */}
          <aside className="w-56 shrink-0 border-r overflow-hidden flex flex-col">
            <ElementListPanel
              elements={elements}
              selectedElementId={selectedElement?.id}
              onElementSelect={setSelectedElement}
              onAddElement={() => setShowAddModal(true)}
              readOnly={isReadOnly}
            />
          </aside>

          {/* Centre: canvas */}
          <main className="flex-1 overflow-hidden">
            {elements.length === 0 ? (
              <div className="flex h-full flex-col items-center justify-center gap-4 text-center">
                <p className="text-muted-foreground">No elements yet.</p>
                {!isReadOnly && (
                  <Button onClick={() => setShowAddModal(true)} variant="outline">
                    <PlusCircle className="mr-2 h-4 w-4" />
                    Add your first element
                  </Button>
                )}
              </div>
            ) : (
              <ArchCanvas
                elements={elements}
                readOnly={isReadOnly}
                drawFlowMode={drawFlowMode}
                selectedElementId={selectedElement?.id}
                onElementSelect={setSelectedElement}
                onEdgeClick={(edgeElementId) => {
                  const edgeElement = elements.find((e) => e.id === edgeElementId);
                  if (edgeElement) setSelectedElement(edgeElement);
                }}
                onCreateDataFlow={handleCreateDataFlow}
                onDeleteElement={(id) => {
                  const el = elements.find((e) => e.id === id);
                  if (el) setKeyboardDeleteElement(el);
                }}
              />
            )}
          </main>

          {/* Right: detail panel */}
          {selectedElement && (
            <aside className="w-72 shrink-0 border-l overflow-hidden">
              <ElementDetailPanel
                element={selectedElement}
                readOnly={isReadOnly}
                onPatch={async (req) => {
                  await patchElement.mutateAsync({ elementId: selectedElement.id, req });
                  toast.success("Element updated");
                }}
                onDelete={async () => {
                  await deleteElement.mutateAsync(selectedElement.id);
                  setSelectedElement(null);
                  toast.success("Element deleted");
                }}
                onCorrect={async (req) => {
                  await correctElement.mutateAsync({ elementId: selectedElement.id, req });
                  toast.success("Correction saved");
                }}
                onSoftRemove={
                  isReadOnly
                    ? undefined
                    : async () => {
                        if (selectedElement.source !== "Extracted") return;
                        const alreadyRemoved = selectedElement.corrections.some(
                          (c) => c.correctionType === "MarkIncorrect",
                        );
                        if (alreadyRemoved) {
                          toast.info("Element is already soft-removed.");
                          return;
                        }
                        await correctElement.mutateAsync({
                          elementId: selectedElement.id,
                          req: {
                            correctionType: "MarkIncorrect",
                            note: "Soft removed by reviewer.",
                          },
                        });
                        setSelectedElement(null);
                        toast.success("Element soft-removed and excluded from analysis");
                      }
                }
              />
            </aside>
          )}
        </div>
      </div>

      {/* F-904: keyboard Delete confirmation */}
      <ConfirmDialog
        open={!!keyboardDeleteElement}
        onOpenChange={(open) => { if (!open) setKeyboardDeleteElement(null); }}
        title="Delete element"
        description={`Delete "${keyboardDeleteElement?.name}"? This cannot be undone.`}
        confirmLabel="Delete"
        confirmVariant="destructive"
        onConfirm={async () => {
          if (!keyboardDeleteElement) return;
          await deleteElement.mutateAsync(keyboardDeleteElement.id);
          if (selectedElement?.id === keyboardDeleteElement.id) setSelectedElement(null);
          setKeyboardDeleteElement(null);
          toast.success("Element deleted");
        }}
      />

      <AddElementModal
        open={showAddModal}
        onOpenChange={setShowAddModal}
        onSubmit={async (req) => {
          await addElement.mutateAsync(req);
          toast.success("Element added");
        }}
      />

      {/* GAP-TH7: pre-analysis concern — available during AwaitingReview */}
      <AddThreatModal
        open={showAddThreatModal}
        onOpenChange={setShowAddThreatModal}
        onSubmit={async (req) => {
          await addThreat.mutateAsync(req);
          toast.success("Threat added — it will be included in the analysis");
        }}
        elements={elements}
        preselectedElementId={selectedElement?.id}
      />

      <ConfirmDialog
        open={showConfirmDialog}
        onOpenChange={setShowConfirmDialog}
        title="Confirm architecture"
        description="This will trigger threat analysis. You cannot make further corrections once confirmed."
        confirmLabel="Confirm and start analysis"
        onConfirm={handleConfirm}
        isLoading={confirmArch.isPending}
      >
        <div className="space-y-3">
          <div className="space-y-1.5">
            <div className="flex items-center justify-between gap-2">
              <Label>Threat methods/frameworks * ({selectedMethods.length} selected)</Label>
              <div className="flex items-center gap-1">
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  className="h-7 px-2 text-xs"
                  onClick={() => setSelectedMethods(METHOD_OPTIONS.map((m) => m.value))}
                >
                  Select all
                </Button>
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  className="h-7 px-2 text-xs"
                  onClick={() => setSelectedMethods([])}
                >
                  Clear all
                </Button>
              </div>
            </div>
            <div className="max-h-44 overflow-auto rounded-md border p-2">
              <div className="grid grid-cols-1 gap-1.5 sm:grid-cols-2">
                {METHOD_OPTIONS.map((opt) => {
                  const checked = selectedMethods.includes(opt.value);
                  return (
                    <label key={opt.value} className="flex items-center gap-2 text-sm">
                      <input
                        type="checkbox"
                        checked={checked}
                        onChange={(e) => {
                          setSelectedMethods((prev) =>
                            e.target.checked
                              ? [...new Set([...prev, opt.value])]
                              : prev.filter((m) => m !== opt.value),
                          );
                        }}
                      />
                      <span>{opt.label}</span>
                    </label>
                  );
                })}
              </div>
            </div>
            <p className="text-xs text-muted-foreground">At least one selection is required.</p>
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="confirmNote">Note (optional)</Label>
            <Textarea
              id="confirmNote"
              value={confirmNote}
              onChange={(e) => setConfirmNote(e.target.value)}
              placeholder="Any notes about this review..."
              rows={3}
            />
          </div>
        </div>
      </ConfirmDialog>
      {/* Deployment context edit dialog */}
      <Dialog open={showDeploymentEdit} onOpenChange={setShowDeploymentEdit}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>Edit deployment context</DialogTitle>
          </DialogHeader>
          <div className="space-y-4 py-2">
            <div className="space-y-1.5">
              <Label>Environment</Label>
              <Select
                value={deploymentEditForm.environment}
                onValueChange={(v) => setDeploymentEditForm((f) => ({ ...f, environment: v }))}
              >
                <SelectTrigger>
                  <SelectValue />
                </SelectTrigger>
                <SelectContent>
                  {["aws", "azure", "gcp", "on_prem", "hybrid", "unknown"].map((e) => (
                    <SelectItem key={e} value={e}>
                      {e === "on_prem" ? "On-Prem" : e.charAt(0).toUpperCase() + e.slice(1)}
                    </SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="flex gap-6">
              <label className="flex items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  checked={deploymentEditForm.containerized}
                  onChange={(e) =>
                    setDeploymentEditForm((f) => ({ ...f, containerized: e.target.checked }))
                  }
                />
                Containerized
              </label>
              <label className="flex items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  checked={deploymentEditForm.serverless}
                  onChange={(e) =>
                    setDeploymentEditForm((f) => ({ ...f, serverless: e.target.checked }))
                  }
                />
                Serverless
              </label>
            </div>
            <div className="space-y-1.5">
              <Label>Infrastructure controls</Label>
              <div className="grid grid-cols-2 gap-1.5">
                {[
                  { value: "waf", label: "WAF" },
                  { value: "cdn", label: "CDN" },
                  { value: "api_gateway", label: "API Gateway" },
                  { value: "load_balancer", label: "Load Balancer" },
                  { value: "ddos_protection", label: "DDoS Protection" },
                ].map((ctrl) => (
                  <label key={ctrl.value} className="flex items-center gap-2 text-sm">
                    <input
                      type="checkbox"
                      checked={deploymentEditForm.infraControls.includes(ctrl.value)}
                      onChange={(e) =>
                        setDeploymentEditForm((f) => ({
                          ...f,
                          infraControls: e.target.checked
                            ? [...new Set([...f.infraControls, ctrl.value])]
                            : f.infraControls.filter((c) => c !== ctrl.value),
                        }))
                      }
                    />
                    {ctrl.label}
                  </label>
                ))}
              </div>
            </div>
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setShowDeploymentEdit(false)}>
              Cancel
            </Button>
            <Button
              disabled={updateDeploymentContext.isPending}
              onClick={async () => {
                try {
                  await updateDeploymentContext.mutateAsync({
                    environment: deploymentEditForm.environment as Parameters<typeof updateDeploymentContext.mutateAsync>[0]["environment"],
                    containerized: deploymentEditForm.containerized,
                    serverless: deploymentEditForm.serverless,
                    infraControls: deploymentEditForm.infraControls,
                  });
                  toast.success("Deployment context updated");
                  setShowDeploymentEdit(false);
                } catch {
                  toast.error("Failed to update deployment context");
                }
              }}
            >
              Save
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </AppShell>
  );
}

