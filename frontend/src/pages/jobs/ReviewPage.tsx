import { useEffect, useState } from "react";
import { useParams, useNavigate, Link } from "react-router-dom";
import { usePageTitle } from "@/hooks/usePageTitle";
import { toast } from "sonner";
import { PlusCircle, ArrowLeft } from "lucide-react";
import {
  useArchitecture,
  useAddElement,
  usePatchElement,
  useDeleteElement,
  useCorrectElement,
  useConfirmArchitecture,
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
import type { ArchitectureElement } from "@/api/architecture";

const METHOD_OPTIONS = [
  { value: "stride", label: "STRIDE" },
  { value: "vast", label: "VAST" },
  { value: "pasta", label: "PASTA" },
  { value: "octave", label: "OCTAVE" },
  { value: "trike", label: "TRIKE" },
  { value: "mitre_attack", label: "MITRE ATT&CK" },
  { value: "owasp_cumulus", label: "OWASP Cumulus" },
  { value: "owasp_cornucopia", label: "OWASP Cornucopia" },
  { value: "linddun", label: "LINDDUN" },
  { value: "abuse_case", label: "Abuse Cases" },
  { value: "tenant_isolation", label: "Tenant Isolation" },
  { value: "identity_session_delegation", label: "Identity/Session Delegation" },
  { value: "ai_llm_threat", label: "AI/LLM Threats" },
  { value: "maestro", label: "MAESTRO (AI)" },
  { value: "emlsg", label: "EMLSG (ML)" },
] as const;

export function ReviewPage() {
  const { orgId, jobId } = useParams<{ orgId: string; jobId: string }>();
  const navigate = useNavigate();

  const { data: job, isLoading: jobLoading } = useJob(orgId!, jobId!);
  const { data: architecture, isLoading: archLoading } = useArchitecture(orgId!, jobId!);

  const addElement = useAddElement(orgId!, jobId!);
  const patchElement = usePatchElement(orgId!, jobId!);
  const deleteElement = useDeleteElement(orgId!, jobId!);
  const correctElement = useCorrectElement(orgId!, jobId!);
  const confirmArch = useConfirmArchitecture(orgId!, jobId!);
  // GAP-TH7: pre-analysis threat/concern addition during AwaitingReview
  const addThreat = useAddThreat(orgId!, jobId!);

  usePageTitle(job ? `${job.title} — Review` : "Architecture Review");

  const [selectedElement, setSelectedElement] = useState<ArchitectureElement | null>(null);
  const [showAddModal, setShowAddModal] = useState(false);
  const [showConfirmDialog, setShowConfirmDialog] = useState(false);
  const [confirmNote, setConfirmNote] = useState("");
  const [selectedMethods, setSelectedMethods] = useState<string[]>([]);
  const [keyboardDeleteElement, setKeyboardDeleteElement] = useState<ArchitectureElement | null>(null);
  const [drawFlowMode, setDrawFlowMode] = useState(false);
  const [showRemovedElements, setShowRemovedElements] = useState(false);
  // GAP-TH7: pre-analysis concern modal
  const [showAddThreatModal, setShowAddThreatModal] = useState(false);

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
      navigate(`/orgs/${orgId!}/jobs/${jobId!}`);
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
            to={`/orgs/${orgId!}/jobs/${jobId!}`}
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
    </AppShell>
  );
}

