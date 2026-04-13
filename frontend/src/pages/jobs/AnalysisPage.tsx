import { useState } from "react";
import { useParams, Link, useNavigate, useSearchParams } from "react-router-dom";
import { usePageTitle } from "@/hooks/usePageTitle";
import { toast } from "sonner";
import { ArrowLeft, RefreshCw, AlertTriangle, HelpCircle } from "lucide-react";
import { useJob } from "@/api/jobs";
import { useThreats, useUpdateThreatStatus, useAddThreatNote, useAddThreat, useAnalysis } from "@/api/threats";
import { useArchitecture, useReanalyzeJob } from "@/api/architecture";
import { AppShell } from "@/components/layout/AppShell";
import { JobStatusBadge } from "@/components/jobs/JobStatusBadge";
import { ThreatCard } from "@/components/threats/ThreatCard";
import { ThreatDetailPanel } from "@/components/threats/ThreatDetailPanel";
import { ThreatFilterBar } from "@/components/threats/ThreatFilterBar";
import { AddThreatModal } from "@/components/threats/AddThreatModal";
import { ArchCanvas } from "@/components/architecture/ArchCanvas";
import { ElementDetailPanel } from "@/components/architecture/ElementDetailPanel";
import { RecommendationsPanel } from "@/components/analysis/RecommendationsPanel";
import { RemediationPanel } from "@/components/analysis/RemediationPanel";
import { ExportPanel } from "@/components/analysis/ExportPanel";
import { ConfirmDialog } from "@/components/common/ConfirmDialog";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Skeleton } from "@/components/ui/skeleton";
import type { Threat } from "@/api/threats";
import type { ArchitectureElement } from "@/api/architecture";
import type { ThreatStatus } from "@/lib/constants";

export function AnalysisPage() {
  const { orgId, jobId } = useParams<{ orgId: string; jobId: string }>();
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const [selectedThreat, setSelectedThreat] = useState<Threat | null>(null);
  const [showAddThreat, setShowAddThreat] = useState(false);
  const [showReanalyzeDialog, setShowReanalyzeDialog] = useState(false);
  // GAP-TH3: selected element from canvas click
  const [selectedElement, setSelectedElement] = useState<ArchitectureElement | null>(null);
  const [activeTab, setActiveTab] = useState("threats");

  const { data: job, isLoading: jobLoading } = useJob(orgId!, jobId!);
  const { data: architecture } = useArchitecture(orgId!, jobId!);
  const { data: analysisData } = useAnalysis(orgId!, jobId!);

  usePageTitle(job ? `${job.title} — Analysis` : "Threat Analysis");

  // GAP-TH3: read elementId from URL search params for server-side filtering
  const elementIdFilter = searchParams.get("elementId") ?? undefined;
  const filters = {
    findingType: searchParams.get("findingType") ?? undefined,
    status: (searchParams.get("status") ?? undefined) as ThreatStatus | undefined,
    elementId: elementIdFilter,
  };
  const { data: threats = [], isLoading: threatsLoading } = useThreats(orgId!, jobId!, filters);

  // Unfiltered threats needed for overlay counts on the diagram
  const { data: allThreats = [] } = useThreats(orgId!, jobId!);

  const updateStatus = useUpdateThreatStatus(orgId!, jobId!);
  const addNote = useAddThreatNote(orgId!, jobId!);
  const addThreat = useAddThreat(orgId!, jobId!);
  const reanalyze = useReanalyzeJob(orgId!, jobId!);

  const canReanalyze = job?.status === "Complete" || job?.status === "Partial";
  const analysis = analysisData as Record<string, unknown> | undefined;

  // Derive node threat counts for ArchCanvas from unfiltered threats
  const threatCountByElement = new Map<string, { count: number; maxSeverity: "critical" | "high" | "medium" | "low" | null }>();
  allThreats.forEach((t) => {
    t.affectedElementIds.forEach((elId) => {
      const existing = threatCountByElement.get(elId);
      threatCountByElement.set(elId, {
        count: (existing?.count ?? 0) + 1,
        maxSeverity: existing?.maxSeverity ?? null,
      });
    });
  });

  // GAP-TH4: per-edge threat counts for DataFlow edge overlays
  const dataFlowElements = architecture?.elements.filter((e) => e.elementType === "DataFlow") ?? [];
  const threatCountByEdge = new Map<string, number>();
  dataFlowElements.forEach((df) => {
    const count = allThreats.filter((t) => t.affectedElementIds.includes(df.id)).length;
    if (count > 0) threatCountByEdge.set(df.id, count);
  });

  // GAP-TH3: derive selected element name for filter chip
  const selectedElementForFilter = elementIdFilter
    ? architecture?.elements.find((e) => e.id === elementIdFilter)
    : undefined;

  // GAP-TH5: threats related to currently selected canvas element
  const threatsForSelectedElement = selectedElement
    ? allThreats.filter((t) => t.affectedElementIds.includes(selectedElement.id))
    : undefined;

  function handleElementSelect(el: ArchitectureElement | null) {
    setSelectedElement(el);
    if (el) {
      // GAP-TH3: write elementId to URL so threat list filters server-side
      const next = new URLSearchParams(searchParams);
      next.set("elementId", el.id);
      setSearchParams(next);
      // Switch to threats tab so user can see filtered results
      setActiveTab("threats");
    } else {
      const next = new URLSearchParams(searchParams);
      next.delete("elementId");
      setSearchParams(next);
    }
  }

  function handleEdgeClick(edgeId: string) {
    const df = dataFlowElements.find((e) => e.id === edgeId);
    if (df) handleElementSelect(df);
  }

  function handleClearElementFilter() {
    setSelectedElement(null);
    const next = new URLSearchParams(searchParams);
    next.delete("elementId");
    setSearchParams(next);
  }

  async function handleReanalyze() {
    try {
      await reanalyze.mutateAsync();
      toast.success("Job reset for re-analysis");
      navigate(`/orgs/${orgId!}/jobs/${jobId!}/review`);
    } catch {
      toast.error("Failed to reset job");
    } finally {
      setShowReanalyzeDialog(false);
    }
  }

  const methodCategories = [...new Set(allThreats.map((t) => t.methodCategory))];

  if (jobLoading) {
    return (
      <AppShell>
        <div className="p-6 space-y-4">
          <Skeleton className="h-8 w-64" />
          <Skeleton className="h-6 w-48" />
          <Skeleton className="h-96 w-full" />
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
            to={`/orgs/${orgId!}/jobs`}
            className="text-muted-foreground hover:text-foreground transition-colors"
            aria-label="Back to jobs"
          >
            <ArrowLeft className="h-5 w-5" />
          </Link>
          <div className="flex-1 min-w-0">
            <h1 className="truncate font-semibold">{job?.title ?? "Threat model"}</h1>
          </div>
          {job && <JobStatusBadge status={job.status} />}

          {canReanalyze && (
            <Button
              variant="outline"
              size="sm"
              onClick={() => setShowReanalyzeDialog(true)}
              className="gap-1.5"
            >
              <RefreshCw className="h-4 w-4" />
              Re-analyze
            </Button>
          )}
        </div>

        {/* Partial banner */}
        {job?.status === "Partial" && (
          <div className="flex items-center gap-2 border-b bg-orange-50 px-4 py-2 text-sm text-orange-800">
            <AlertTriangle className="h-4 w-4 shrink-0" />
            This analysis is incomplete due to architectural ambiguity. Some threats may be missing.
          </div>
        )}

        {/* Summary header */}
        {analysis && (
          <div className="border-b px-4 py-3 shrink-0 space-y-2">
            {typeof analysis["systemSummary"] === "string" && (
              <p className="text-sm text-muted-foreground line-clamp-2">{analysis["systemSummary"]}</p>
            )}
            <div className="flex flex-wrap items-center gap-2">
              {Array.isArray(analysis["classification"]) &&
                (analysis["classification"] as string[]).map((c) => (
                  <Badge key={c} variant="outline" className="text-xs">{c}</Badge>
                ))}
              <Badge variant="secondary" className="text-xs">
                {allThreats.length} threats
              </Badge>
            </div>

            {/* Review questions */}
            {Array.isArray(analysis["reviewQuestions"]) &&
              (analysis["reviewQuestions"] as string[]).length > 0 && (
                <div className="rounded-md bg-amber-50 border border-amber-200 p-3">
                  <div className="flex items-center gap-2 text-amber-800 text-xs font-semibold mb-1">
                    <HelpCircle className="h-3.5 w-3.5" />
                    Questions requiring your review
                  </div>
                  <ul className="space-y-1">
                    {(analysis["reviewQuestions"] as string[]).map((q, i) => (
                      <li key={i} className="text-xs text-amber-700">{q}</li>
                    ))}
                  </ul>
                </div>
              )}
          </div>
        )}

        {/* Tabs */}
        <div className="flex-1 overflow-hidden">
          <Tabs value={activeTab} onValueChange={setActiveTab} className="flex h-full flex-col">
            <TabsList className="mx-4 mt-2 w-fit shrink-0">
              <TabsTrigger value="threats">
                Threats ({elementIdFilter ? `${threats.length} / ${allThreats.length}` : allThreats.length})
              </TabsTrigger>
              <TabsTrigger value="architecture">Architecture</TabsTrigger>
              <TabsTrigger value="recommendations">Recommendations</TabsTrigger>
              <TabsTrigger value="remediation">Remediation</TabsTrigger>
              <TabsTrigger value="export">Export</TabsTrigger>
            </TabsList>

            {/* Threats tab */}
            <TabsContent value="threats" className="flex flex-1 overflow-hidden mt-0">
              {/* Left: filter + list */}
              <div className="flex w-80 shrink-0 flex-col gap-3 overflow-y-auto border-r p-3">
                {/* GAP-TH3: pass element filter info to filter bar */}
                <ThreatFilterBar
                  methodCategories={methodCategories}
                  elementFilter={selectedElementForFilter
                    ? { id: selectedElementForFilter.id, name: selectedElementForFilter.name }
                    : undefined}
                  onClearElement={handleClearElementFilter}
                />
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => setShowAddThreat(true)}
                  className="w-full"
                >
                  + Add your own threat
                </Button>
                {threatsLoading ? (
                  <div className="space-y-2">
                    {[1, 2, 3].map((i) => <Skeleton key={i} className="h-24 w-full" />)}
                  </div>
                ) : threats.length === 0 ? (
                  <p className="text-center text-sm text-muted-foreground py-6">No threats match your filters.</p>
                ) : (
                  <div className="space-y-2">
                    {threats.map((t) => (
                      <ThreatCard
                        key={t.id}
                        threat={t}
                        selected={selectedThreat?.id === t.id}
                        onClick={setSelectedThreat}
                      />
                    ))}
                  </div>
                )}
              </div>

              {/* Right: detail panel */}
              <div className="flex-1 overflow-hidden">
                {selectedThreat ? (
                  <ThreatDetailPanel
                    threat={selectedThreat}
                    onClose={() => setSelectedThreat(null)}
                    onUpdateStatus={async (id, status) => {
                      await updateStatus.mutateAsync({ threatId: id, status });
                    }}
                    onAddNote={async (id, body) => {
                      await addNote.mutateAsync({ threatId: id, body });
                    }}
                  />
                ) : (
                  <div className="flex h-full items-center justify-center text-muted-foreground text-sm">
                    Select a threat to view details
                  </div>
                )}
              </div>
            </TabsContent>

            {/* Architecture tab — GAP-TH3/TH4/TH5 */}
            <TabsContent value="architecture" className="flex flex-1 overflow-hidden mt-0">
              {architecture ? (
                <>
                  <div className="flex-1 overflow-hidden">
                    <ArchCanvas
                      elements={architecture.elements}
                      readOnly
                      threatCountByElement={threatCountByElement}
                      threatCountByEdge={threatCountByEdge}
                      selectedElementId={selectedElement?.id}
                      onElementSelect={handleElementSelect}
                      onEdgeClick={handleEdgeClick}
                    />
                  </div>
                  {/* GAP-TH5: per-element panel shown when element is selected */}
                  {selectedElement && (
                    <div className="w-72 shrink-0 border-l overflow-y-auto">
                      <ElementDetailPanel
                        element={selectedElement}
                        readOnly
                        onPatch={async () => undefined}
                        onDelete={async () => undefined}
                        onCorrect={async () => undefined}
                        relatedThreats={threatsForSelectedElement}
                        onThreatClick={(t) => {
                          setSelectedThreat(t);
                          setActiveTab("threats");
                        }}
                      />
                    </div>
                  )}
                </>
              ) : (
                <div className="flex h-full items-center justify-center text-muted-foreground text-sm">
                  Architecture not available
                </div>
              )}
            </TabsContent>

            {/* Recommendations tab */}
            <TabsContent value="recommendations" className="flex-1 overflow-y-auto mt-0">
              <RecommendationsPanel
                recommendations={
                  Array.isArray(analysis?.["secureDesignRecommendations"])
                    ? (analysis?.["secureDesignRecommendations"] as Array<{ title: string; description: string; principles?: string[]; affectedElements?: string[] }>)
                    : []
                }
              />
            </TabsContent>

            {/* Remediation tab */}
            <TabsContent value="remediation" className="flex-1 overflow-y-auto mt-0">
              <RemediationPanel
                items={
                  Array.isArray(analysis?.["prioritizedRemediationList"])
                    ? (analysis?.["prioritizedRemediationList"] as Array<{ threatIdentifier: string; title: string; mitigationSummary: string; priority: "critical" | "high" | "medium" | "low" }>)
                    : []
                }
                onThreatClick={(identifier) => {
                  const threat = allThreats.find((t) => t.identifier === identifier);
                  if (threat) {
                    setSelectedThreat(threat);
                    setActiveTab("threats");
                  }
                }}
              />
            </TabsContent>

            {/* Export tab */}
            <TabsContent value="export" className="flex-1 overflow-y-auto mt-0">
              <ExportPanel orgId={orgId!} jobId={jobId!} analysisData={analysisData} />
            </TabsContent>
          </Tabs>
        </div>
      </div>

      <AddThreatModal
        open={showAddThreat}
        onOpenChange={setShowAddThreat}
        onSubmit={async (req) => {
          await addThreat.mutateAsync(req);
          toast.success("Threat added");
        }}
        elements={architecture?.elements}
        preselectedElementId={elementIdFilter}
      />

      <ConfirmDialog
        open={showReanalyzeDialog}
        onOpenChange={setShowReanalyzeDialog}
        title="Re-analyze"
        description="This will reset the architecture review and delete all system-generated threats. Your manually added threats will be preserved."
        confirmLabel="Re-analyze"
        onConfirm={handleReanalyze}
        isLoading={reanalyze.isPending}
      />
    </AppShell>
  );
}
