import { useEffect, useMemo, useState } from "react";
import { useParams, Link, useNavigate, useSearchParams } from "react-router-dom";
import { usePageTitle } from "@/hooks/usePageTitle";
import { toast } from "sonner";
import { ArrowLeft, RefreshCw, AlertTriangle, HelpCircle, Clock, DollarSign, Copy } from "lucide-react";
import { useJob } from "@/api/jobs";
import { useThreats, useUpdateThreatStatus, useAddThreatNote, useAddThreat, useAnalysis, useRejectedCandidates, type RejectedCandidate } from "@/api/threats";
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
import { requiredParam } from "@/lib/requiredParam";

const SEVERITY_ORDER: Record<string, number> = { critical: 0, high: 1, medium: 2, low: 3, note: 4 };

function formatDuration(ms: number): string {
  const totalSeconds = Math.round(ms / 1000);
  if (totalSeconds < 60) return `${totalSeconds}s`;
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return seconds > 0 ? `${minutes}m ${seconds}s` : `${minutes}m`;
}

export function AnalysisPage() {
  const params = useParams<{ orgId: string; jobId: string }>();
  const orgId = requiredParam(params.orgId, "orgId");
  const jobId = requiredParam(params.jobId, "jobId");
  const navigate = useNavigate();
  const [searchParams, setSearchParams] = useSearchParams();
  const [selectedThreat, setSelectedThreat] = useState<Threat | null>(null);
  const [showAddThreat, setShowAddThreat] = useState(false);
  const [draftFromRejected, setDraftFromRejected] = useState<RejectedCandidate | null>(null);
  const [showReanalyzeDialog, setShowReanalyzeDialog] = useState(false);
  // GAP-TH3: selected element from canvas click
  const [selectedElement, setSelectedElement] = useState<ArchitectureElement | null>(null);
  const [activeTab, setActiveTab] = useState("threats");

  const { data: job, isLoading: jobLoading } = useJob(orgId, jobId);
  const { data: architecture } = useArchitecture(orgId, jobId);
  const { data: analysisData } = useAnalysis(orgId, jobId);

  usePageTitle(job ? `${job.title} — Analysis` : "Threat Analysis");

  // GAP-TH3: read elementId from URL search params for server-side filtering
  const elementIdFilter = searchParams.get("elementId") ?? undefined;
  const statusFilters = searchParams
    .getAll("status")
    .filter((v): v is ThreatStatus => v === "Open" || v === "Accepted" || v === "Mitigated" || v === "Rejected");
  const findingTypeFilters = searchParams.getAll("findingType").filter((v) => !!v.trim());
  const confidenceFilters = searchParams
    .getAll("confidence")
    .filter((v): v is "High" | "Medium" | "Low" => v === "High" || v === "Medium" || v === "Low");
  const methodFilters = searchParams.getAll("method").filter((v) => !!v.trim());
  const frameworkFilters = searchParams.getAll("framework").filter((v) => !!v.trim());

  // Defensive guard: stale deep links can carry element IDs from older architecture versions.
  // If the element no longer exists, clear the URL filter instead of letting downstream queries fail.
  useEffect(() => {
    if (!elementIdFilter || !architecture) return;
    const exists = architecture.elements.some((e) => e.id === elementIdFilter);
    if (!exists) {
      const next = new URLSearchParams(searchParams);
      next.delete("elementId");
      setSearchParams(next, { replace: true });
      setSelectedElement(null);
      toast.info("Element filter was cleared because the element no longer exists.");
    }
  }, [architecture, elementIdFilter, searchParams, setSearchParams]);

  const filters = {
    findingType: findingTypeFilters.length > 0 ? findingTypeFilters : undefined,
    status: statusFilters.length > 0 ? statusFilters : undefined,
    confidence: confidenceFilters.length > 0 ? confidenceFilters : undefined,
    method: methodFilters.length > 0 ? methodFilters : undefined,
    framework: frameworkFilters.length > 0 ? frameworkFilters : undefined,
    elementId: elementIdFilter,
  };
  const { data: threats = [], isLoading: threatsLoading } = useThreats(orgId, jobId, filters);
  const { data: rejectedCandidates = [], isLoading: rejectedLoading } = useRejectedCandidates(orgId, jobId);

  // Unfiltered threats needed for overlay counts on the diagram
  const { data: allThreats = [] } = useThreats(orgId, jobId);

  const updateStatus = useUpdateThreatStatus(orgId, jobId);
  const addNote = useAddThreatNote(orgId, jobId);
  const addThreat = useAddThreat(orgId, jobId);
  const reanalyze = useReanalyzeJob(orgId, jobId);

  const canReanalyze = job?.status === "Complete" || job?.status === "Partial";
  const analysis = analysisData as Record<string, unknown> | undefined;

  const sourceMethodsByIdentifier = useMemo(() => {
    const map = new Map<string, string[]>();
    if (!analysis) return map;

    type AnalysisThreat = { identifier?: string; sourceMethods?: string[] };
    const groups = [
      ...(Array.isArray(analysis["confirmedThreats"]) ? (analysis["confirmedThreats"] as AnalysisThreat[]) : []),
      ...(Array.isArray(analysis["conditionalThreats"]) ? (analysis["conditionalThreats"] as AnalysisThreat[]) : []),
    ];

    groups.forEach((threat) => {
      if (!threat.identifier) return;
      const methods = Array.isArray(threat.sourceMethods)
        ? [...new Set(threat.sourceMethods.filter((m) => typeof m === "string" && m.trim().length > 0))]
        : [];
      map.set(threat.identifier, methods);
    });

    return map;
  }, [analysis]);

  const displayedThreats = useMemo(
    () =>
      threats
        .map((t) => ({
          ...t,
          sourceMethods: sourceMethodsByIdentifier.get(t.identifier) ?? t.sourceMethods ?? [],
        }))
        .sort((a, b) => {
          const sa = SEVERITY_ORDER[a.riskRating?.severity ?? "note"] ?? 99;
          const sb = SEVERITY_ORDER[b.riskRating?.severity ?? "note"] ?? 99;
          return sa - sb;
        }),
    [sourceMethodsByIdentifier, threats],
  );

  const displayedAllThreats = useMemo(
    () =>
      allThreats.map((t) => ({
        ...t,
        sourceMethods: sourceMethodsByIdentifier.get(t.identifier) ?? t.sourceMethods ?? [],
      })),
    [allThreats, sourceMethodsByIdentifier],
  );

  // Derive node threat counts for ArchCanvas from unfiltered threats
  const threatCountByElement = new Map<string, { count: number; maxSeverity: "critical" | "high" | "medium" | "low" | null }>();
  displayedAllThreats.forEach((t) => {
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
    const count = displayedAllThreats.filter((t) => t.affectedElementIds.includes(df.id)).length;
    if (count > 0) threatCountByEdge.set(df.id, count);
  });

  // GAP-TH3: derive selected element name for filter chip
  const selectedElementForFilter = elementIdFilter
    ? architecture?.elements.find((e) => e.id === elementIdFilter)
    : undefined;

  // GAP-TH5: threats related to currently selected canvas element
  const threatsForSelectedElement = selectedElement
    ? displayedAllThreats.filter((t) => t.affectedElementIds.includes(selectedElement.id))
    : undefined;
  function handleElementSelect(el: ArchitectureElement | null) {
    setSelectedElement(el);
    if (el) {
      // GAP-TH3: write elementId to URL so threat list filters server-side
      const next = new URLSearchParams(searchParams);
      next.set("elementId", el.id);
      setSearchParams(next);
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

  function handleShowThreatInArchitecture(threat: Threat) {
    const firstMatch = threat.affectedElementIds
      .map((id) => architecture?.elements.find((e) => e.id === id) ?? null)
      .find((e): e is ArchitectureElement => e !== null);

    if (!firstMatch) {
      toast.info("No mapped architecture element found for this threat.");
      return;
    }

    setSelectedElement(firstMatch);
    const next = new URLSearchParams(searchParams);
    next.set("elementId", firstMatch.id);
    setSearchParams(next);
    setActiveTab("architecture");
  }

  async function handleReanalyze() {
    try {
      await reanalyze.mutateAsync();
      toast.success("Job reset for re-analysis");
      navigate(`/orgs/${orgId}/jobs/${jobId}/review`);
    } catch {
      toast.error("Failed to reset job");
    } finally {
      setShowReanalyzeDialog(false);
    }
  }

  const methodCategories = [...new Set(displayedAllThreats.map((t) => t.methodCategory))];
  const frameworks = [
    ...new Set(displayedAllThreats.flatMap((t) => (t.frameworkMappings ?? []).map((m) => m.framework))),
  ];

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
      <div className="flex h-full min-h-0 flex-col">
        {/* Top bar */}
        <div className="flex shrink-0 flex-wrap items-center gap-3 border-b px-4 py-3">
          <Link
            to={`/orgs/${orgId}/jobs`}
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
          <div className="flex items-start gap-2 border-b bg-orange-50 px-4 py-2 text-sm text-orange-800">
            <AlertTriangle className="h-4 w-4 shrink-0 mt-0.5" />
            <span>
              {typeof analysis?.["partialReason"] === "string"
                ? analysis["partialReason"]
                : "This analysis is incomplete due to architectural ambiguity. Some threats may be missing."}
            </span>
          </div>
        )}

        {/* Usage summary */}
        {job?.usageSummary && (
          <div className="flex flex-wrap items-center gap-4 border-b px-4 py-2 text-xs text-muted-foreground">
            <span className="flex items-center gap-1">
              <Clock className="h-3.5 w-3.5" />
              {formatDuration(job.usageSummary.elapsedMs)}
            </span>
            <span className="font-mono">
              in {job.usageSummary.totalInputTokens.toLocaleString()} / out {job.usageSummary.totalOutputTokens.toLocaleString()} tok
            </span>
            {job.usageSummary.estimatedCostUsd != null && (
              <span className="flex items-center gap-1">
                <DollarSign className="h-3.5 w-3.5" />
                {job.usageSummary.estimatedCostUsd < 0.01
                  ? "<$0.01"
                  : `$${job.usageSummary.estimatedCostUsd.toFixed(4)}`}
              </span>
            )}
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
                {displayedAllThreats.length} threats
              </Badge>
              <Badge variant="outline" className="text-xs">
                {rejectedCandidates.length} discarded
              </Badge>
            </div>

            {(job?.applicationDescription || job?.architectureDescription) && (
              <details className="rounded-md border bg-muted/30">
                <summary className="cursor-pointer select-none px-3 py-2 text-xs font-semibold uppercase tracking-wide text-muted-foreground hover:text-foreground">
                  System description
                </summary>
                <div className="space-y-2 px-3 pb-3 pt-1">
                  {job.applicationDescription && (
                    <div>
                      <p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">Application</p>
                      <p className="text-sm">{job.applicationDescription}</p>
                    </div>
                  )}
                  {job.architectureDescription && (
                    <div>
                      <p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">Architecture</p>
                      <p className="text-sm">{job.architectureDescription}</p>
                    </div>
                  )}
                </div>
              </details>
            )}

            {/* Review questions */}
            {Array.isArray(analysis["reviewQuestions"]) &&
              (analysis["reviewQuestions"] as string[]).length > 0 && (
                <div className="rounded-md bg-amber-50 border border-amber-200 p-3">
                  <div className="flex items-center justify-between mb-1">
                    <div className="flex items-center gap-2 text-amber-800 text-xs font-semibold">
                      <HelpCircle className="h-3.5 w-3.5" />
                      Questions requiring your review
                    </div>
                    <button
                      onClick={() => {
                        const text = (analysis["reviewQuestions"] as string[]).join("\n\n");
                        void navigator.clipboard.writeText(text);
                        toast.success("Questions copied");
                      }}
                      className="flex items-center gap-1 text-amber-700 hover:text-amber-900 text-xs transition-colors"
                    >
                      <Copy className="h-3 w-3" />
                      Copy all
                    </button>
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
        <div className="min-h-0 flex-1 overflow-hidden">
          <Tabs value={activeTab} onValueChange={setActiveTab} className="flex h-full min-h-0 flex-col">
            <TabsList className="mx-4 mt-2 w-auto max-w-[calc(100vw-2rem)] shrink-0 justify-start overflow-x-auto whitespace-nowrap">
              <TabsTrigger value="threats">
                Threats ({elementIdFilter ? `${displayedThreats.length} / ${displayedAllThreats.length}` : displayedAllThreats.length})
              </TabsTrigger>
              <TabsTrigger value="discarded">Discarded threats ({rejectedCandidates.length})</TabsTrigger>
              <TabsTrigger value="architecture">Architecture</TabsTrigger>
              <TabsTrigger value="recommendations">Recommendations</TabsTrigger>
              <TabsTrigger value="remediation">Remediation</TabsTrigger>
              <TabsTrigger value="export">Export</TabsTrigger>
            </TabsList>

            {/* Threats tab */}
            {activeTab === "threats" && (
            <TabsContent value="threats" className="mt-0 flex h-full min-h-0 flex-1 flex-col overflow-hidden md:flex-row md:items-stretch">
              {/* Left: filter + list */}
              <div className="flex w-full shrink-0 flex-col gap-3 border-b p-3 md:h-full md:w-[52rem] md:border-b-0 md:border-r">
                <ThreatFilterBar
                  methodCategories={methodCategories}
                  frameworks={frameworks}
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
                <div className="min-h-0 flex-1 overflow-y-auto">
                  {threatsLoading ? (
                    <div className="space-y-1">
                      {[1, 2, 3].map((i) => <Skeleton key={i} className="h-9 w-full" />)}
                    </div>
                  ) : threats.length === 0 ? (
                    <p className="py-6 text-center text-sm text-muted-foreground">No threats match your filters.</p>
                  ) : (
                    <div className="space-y-1">
                      {displayedThreats.map((t) => (
                        <ThreatCard
                          key={t.id}
                          threat={t}
                          selected={selectedThreat?.id === t.id}
                          onClick={setSelectedThreat}
                          onShowInArchitecture={handleShowThreatInArchitecture}
                        />
                      ))}
                    </div>
                  )}
                </div>
              </div>

              {/* Right: detail panel — only rendered when a threat is selected */}
              {selectedThreat && (
                <div className="min-h-0 flex-1 overflow-hidden md:h-full">
                  <ThreatDetailPanel
                    threat={selectedThreat}
                    onClose={() => setSelectedThreat(null)}
                    onUpdateStatus={async (id, status) => {
                      await updateStatus.mutateAsync({ threatId: id, status });
                    }}
                    onAddNote={async (id, body) => {
                      await addNote.mutateAsync({ threatId: id, body });
                    }}
                    onShowInArchitecture={handleShowThreatInArchitecture}
                  />
                </div>
              )}
            </TabsContent>
            )}

            {/* Architecture tab — GAP-TH3/TH4/TH5 */}
            {activeTab === "discarded" && (
            <TabsContent value="discarded" className="mt-0 flex h-full min-h-0 flex-1 flex-col overflow-hidden">
              <div className="border-b px-4 py-3 text-sm text-muted-foreground">
                Threats rejected during analysis/synthesis. You can manually promote any discarded threat into your threat list.
              </div>
              <div className="min-h-0 flex-1 overflow-y-auto p-4">
                {rejectedLoading ? (
                  <div className="space-y-2">
                    {[1, 2, 3].map((i) => <Skeleton key={i} className="h-28 w-full" />)}
                  </div>
                ) : rejectedCandidates.length === 0 ? (
                  <p className="py-6 text-center text-sm text-muted-foreground">No discarded threats for this job.</p>
                ) : (
                  <div className="space-y-3">
                    {rejectedCandidates.map((candidate) => (
                      <div key={candidate.id} className="rounded-lg border p-4">
                        <div className="flex items-start justify-between gap-3">
                          <div>
                            <h3 className="font-medium">{candidate.title}</h3>
                            <div className="mt-1 flex flex-wrap items-center gap-1.5">
                              {candidate.methodCategory && (
                                <Badge variant="outline" className="text-xs">{candidate.methodCategory}</Badge>
                              )}
                              <Badge variant="secondary" className="text-xs">{candidate.rejectionReason}</Badge>
                            </div>
                          </div>
                          <Button
                            size="sm"
                            variant="outline"
                            onClick={() => {
                              setDraftFromRejected(candidate);
                              setShowAddThreat(true);
                            }}
                          >
                            Promote to threat
                          </Button>
                        </div>
                        {candidate.rejectionNote && (
                          <p className="mt-2 text-sm text-muted-foreground whitespace-pre-wrap">{candidate.rejectionNote}</p>
                        )}
                      </div>
                    ))}
                  </div>
                )}
              </div>
            </TabsContent>
            )}

            {/* Architecture tab — GAP-TH3/TH4/TH5 */}
            {activeTab === "architecture" && (
            <TabsContent value="architecture" className="mt-0 flex h-full min-h-0 flex-1 items-stretch overflow-hidden">
              {architecture ? (
                <>
                  <div className="min-h-0 flex-1 overflow-hidden">
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
                  {/* Right detail panel — element or threat, shown on selection */}
                  {(selectedThreat ?? selectedElement) && (
                    <div className="min-h-0 w-full shrink-0 overflow-y-auto border-l xl:h-full xl:w-[24rem]">
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
                      ) : selectedElement ? (
                        <ElementDetailPanel
                          element={selectedElement}
                          readOnly
                          onPatch={async () => undefined}
                          onDelete={async () => undefined}
                          onCorrect={async () => undefined}
                          relatedThreats={threatsForSelectedElement}
                          onThreatClick={(t) => {
                            setSelectedThreat(t);
                          }}
                        />
                      ) : null}
                    </div>
                  )}
                </>
              ) : (
                <div className="flex h-full items-center justify-center text-muted-foreground text-sm">
                  Architecture not available
                </div>
              )}
            </TabsContent>
            )}

            {/* Recommendations tab */}
            {activeTab === "recommendations" && (
            <TabsContent value="recommendations" className="flex-1 overflow-y-auto mt-0">
              <RecommendationsPanel
                recommendations={
                  Array.isArray(analysis?.["secureDesignRecommendations"])
                    ? (analysis?.["secureDesignRecommendations"] as Array<{ title: string; description: string; principles?: string[]; affectedElements?: string[] }>)
                    : []
                }
              />
            </TabsContent>
            )}

            {/* Remediation tab */}
            {activeTab === "remediation" && (
            <TabsContent value="remediation" className="flex-1 overflow-y-auto mt-0">
              <RemediationPanel
                items={
                  Array.isArray(analysis?.["prioritizedRemediationList"])
                    ? (analysis?.["prioritizedRemediationList"] as Array<{ threatIdentifier: string; title: string; mitigationSummary: string; priority: "critical" | "high" | "medium" | "low" }>)
                    : []
                }
                onThreatClick={(identifier) => {
                  const threat = displayedAllThreats.find((t) => t.identifier === identifier);
                  if (threat) {
                    setSelectedThreat(threat);
                    setActiveTab("threats");
                  }
                }}
              />
            </TabsContent>
            )}

            {/* Export tab */}
            {activeTab === "export" && (
            <TabsContent value="export" className="flex-1 overflow-y-auto mt-0">
              <ExportPanel orgId={orgId} jobId={jobId} analysisData={analysisData} architecture={architecture} threats={allThreats} />
            </TabsContent>
            )}
          </Tabs>
        </div>
      </div>

      <AddThreatModal
        open={showAddThreat}
        onOpenChange={(open) => {
          setShowAddThreat(open);
          if (!open) setDraftFromRejected(null);
        }}
        onSubmit={async (req) => {
          await addThreat.mutateAsync(req);
          toast.success("Threat added");
          setDraftFromRejected(null);
        }}
        elements={architecture?.elements}
        preselectedElementId={elementIdFilter}
        initialValues={draftFromRejected
          ? {
              title: draftFromRejected.title,
              methodCategory: draftFromRejected.methodCategory ?? "manual_review",
              description: draftFromRejected.rejectionNote ?? "Candidate promoted by reviewer for manual inclusion.",
              attackScenario: draftFromRejected.rejectionNote ?? "Reviewer promoted this discarded candidate; define concrete attacker path.",
            }
          : undefined}
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
