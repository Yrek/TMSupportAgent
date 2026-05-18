import { useEffect, useMemo, useRef, useState } from "react";
import { useParams, Link, useNavigate, useSearchParams } from "react-router-dom";
import { usePageTitle } from "@/hooks/usePageTitle";
import { toast } from "sonner";
import { ArrowLeft, RefreshCw, AlertTriangle, HelpCircle, Clock, DollarSign, Copy, Zap, Search, CheckCircle, ChevronsUpDown } from "lucide-react";
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
import { TestCasesPanel } from "@/components/analysis/TestCasesPanel";
import { AttackTreesPanel } from "@/components/analysis/AttackTreesPanel";
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
import { cn } from "@/lib/utils";

const SEVERITY_ORDER: Record<string, number> = { critical: 0, high: 1, medium: 2, low: 3, note: 4 };

const SEVERITY_STYLE = {
  critical: { dot: "bg-red-600",    text: "text-red-700",    label: "Critical" },
  high:     { dot: "bg-orange-500", text: "text-orange-700", label: "High" },
  medium:   { dot: "bg-amber-400",  text: "text-amber-700",  label: "Medium" },
  low:      { dot: "bg-blue-400",   text: "text-blue-700",   label: "Low" },
};

function formatDuration(ms: number): string {
  const totalSeconds = Math.round(ms / 1000);
  if (totalSeconds < 60) return `${totalSeconds}s`;
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return seconds > 0 ? `${minutes}m ${seconds}s` : `${minutes}m`;
}

// ── Severity + review progress bar ────────────────────────────────────────────
function SeverityBar({ threats }: { threats: Threat[] }) {
  const counts = useMemo(() => {
    const c = { critical: 0, high: 0, medium: 0, low: 0 };
    threats.forEach((t) => {
      const s = t.riskRating?.severity as keyof typeof c | undefined;
      if (s && s in c) c[s]++;
    });
    return c;
  }, [threats]);

  const reviewed = threats.filter((t) => t.status !== "Open").length;
  const total = threats.length;
  const reviewPct = total > 0 ? Math.round((reviewed / total) * 100) : 0;

  return (
    <div className="flex flex-wrap items-center gap-x-4 gap-y-1 border-b px-4 py-2 text-xs">
      {(["critical", "high", "medium", "low"] as const).map((sev) =>
        counts[sev] > 0 ? (
          <span key={sev} className="flex items-center gap-1.5">
            <span className={cn("h-2 w-2 rounded-full", SEVERITY_STYLE[sev].dot)} />
            <span className={cn("font-semibold", SEVERITY_STYLE[sev].text)}>
              {counts[sev]} {SEVERITY_STYLE[sev].label}
            </span>
          </span>
        ) : null,
      )}
      <span className="ml-auto flex items-center gap-2 text-muted-foreground">
        <span>{reviewed}/{total} reviewed</span>
        <div className="h-1.5 w-20 rounded-full bg-muted overflow-hidden">
          <div
            className="h-full rounded-full bg-green-500 transition-all"
            style={{ width: `${reviewPct}%` }}
          />
        </div>
      </span>
    </div>
  );
}

// ── Top threat drivers panel (shown in right panel when no threat selected) ───
function TopDriversPanel({
  threats,
  elements,
  onElementClick,
}: {
  threats: Threat[];
  elements: ArchitectureElement[];
  onElementClick: (el: ArchitectureElement) => void;
}) {
  const drivers = useMemo(() => {
    const map = new Map<string, { el: ArchitectureElement; count: number; maxSev: string }>();
    threats.forEach((t) => {
      t.affectedElementIds.forEach((id) => {
        const el = elements.find((e) => e.id === id);
        if (!el || el.elementType === "DataFlow") return;
        const existing = map.get(id);
        const sev = t.riskRating?.severity ?? "note";
        if (existing) {
          existing.count++;
          if ((SEVERITY_ORDER[sev] ?? 99) < (SEVERITY_ORDER[existing.maxSev] ?? 99))
            existing.maxSev = sev;
        } else {
          map.set(id, { el, count: 1, maxSev: sev });
        }
      });
    });
    return [...map.values()].sort((a, b) => b.count - a.count).slice(0, 6);
  }, [threats, elements]);

  if (drivers.length === 0) return null;

  return (
    <div className="border-l h-full overflow-y-auto p-4 w-64 shrink-0">
      <p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground mb-3 flex items-center gap-1.5">
        <Zap className="h-3.5 w-3.5" />
        Top threat drivers
      </p>
      <div className="space-y-1">
        {drivers.map(({ el, count, maxSev }) => (
          <button
            key={el.id}
            onClick={() => onElementClick(el)}
            className="flex w-full items-center justify-between rounded-md px-2 py-1.5 text-left text-sm hover:bg-muted transition-colors"
          >
            <span className="truncate text-sm">{el.name}</span>
            <span className={cn(
              "ml-2 shrink-0 rounded-full px-1.5 py-0.5 text-[10px] font-semibold",
              maxSev === "critical" ? "bg-red-100 text-red-700" :
              maxSev === "high"     ? "bg-orange-100 text-orange-700" :
              maxSev === "medium"   ? "bg-amber-100 text-amber-700" :
                                      "bg-blue-100 text-blue-700",
            )}>
              {count}
            </span>
          </button>
        ))}
      </div>
    </div>
  );
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
  const [selectedElement, setSelectedElement] = useState<ArchitectureElement | null>(null);
  const [activeTab, setActiveTab] = useState("threats");

  const { data: job, isLoading: jobLoading } = useJob(orgId, jobId);
  const { data: architecture } = useArchitecture(orgId, jobId);
  const { data: analysisData } = useAnalysis(orgId, jobId);

  usePageTitle(job ? `${job.title} — Analysis` : "Threat Analysis");

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
        .map((t) => ({ ...t, sourceMethods: sourceMethodsByIdentifier.get(t.identifier) ?? t.sourceMethods ?? [] }))
        .sort((a, b) => {
          const sa = SEVERITY_ORDER[a.riskRating?.severity ?? "note"] ?? 99;
          const sb = SEVERITY_ORDER[b.riskRating?.severity ?? "note"] ?? 99;
          return sa - sb;
        }),
    [sourceMethodsByIdentifier, threats],
  );

  const displayedAllThreats = useMemo(
    () => allThreats.map((t) => ({ ...t, sourceMethods: sourceMethodsByIdentifier.get(t.identifier) ?? t.sourceMethods ?? [] })),
    [allThreats, sourceMethodsByIdentifier],
  );

  const threatCountByElement = new Map<string, { count: number; maxSeverity: "critical" | "high" | "medium" | "low" | null }>();
  displayedAllThreats.forEach((t) => {
    const sev = t.riskRating?.severity as "critical" | "high" | "medium" | "low" | undefined;
    t.affectedElementIds.forEach((elId) => {
      const existing = threatCountByElement.get(elId);
      const prevSev = existing?.maxSeverity ?? null;
      const nextSev = (sev && (!prevSev || (SEVERITY_ORDER[sev] ?? 99) < (SEVERITY_ORDER[prevSev] ?? 99)))
        ? sev
        : prevSev;
      threatCountByElement.set(elId, { count: (existing?.count ?? 0) + 1, maxSeverity: nextSev });
    });
  });

  const dataFlowElements = architecture?.elements.filter((e) => e.elementType === "DataFlow") ?? [];
  const threatCountByEdge = new Map<string, number>();
  dataFlowElements.forEach((df) => {
    const count = displayedAllThreats.filter((t) => t.affectedElementIds.includes(df.id)).length;
    if (count > 0) threatCountByEdge.set(df.id, count);
  });

  const selectedElementForFilter = elementIdFilter
    ? architecture?.elements.find((e) => e.id === elementIdFilter)
    : undefined;

  const threatsForSelectedElement = selectedElement
    ? displayedAllThreats.filter((t) => t.affectedElementIds.includes(selectedElement.id))
    : undefined;

  // Evidence gaps: unanswered pre-analysis questions
  const unansweredCount = useMemo(() => {
    if (!architecture?.clarificationQuestions?.length) return 0;
    const answeredSet = new Set((architecture.clarificationAnswers ?? []).map((a) => a.question));
    return architecture.clarificationQuestions.filter((q) => !answeredSet.has(q.question)).length;
  }, [architecture]);

  const reviewQuestions = Array.isArray(analysis?.["reviewQuestions"])
    ? (analysis!["reviewQuestions"] as string[])
    : [];

  function handleElementSelect(el: ArchitectureElement | null) {
    setSelectedElement(el);
    if (el) {
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

  function handleDriverClick(el: ArchitectureElement) {
    handleElementSelect(el);
    setActiveTab("threats");
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

  const searchQuery = searchParams.get("q")?.trim().toLowerCase() ?? "";
  const visibleThreats = searchQuery
    ? displayedThreats.filter((t) => t.title.toLowerCase().includes(searchQuery))
    : displayedThreats;

  // Auto-clear selected threat when it falls outside the current filter/search result
  useEffect(() => {
    if (selectedThreat && !visibleThreats.some((t) => t.id === selectedThreat.id)) {
      setSelectedThreat(null);
    }
  }, [visibleThreats, selectedThreat]);

  const listRef = useRef<HTMLDivElement>(null);

  function handleListKeyDown(e: React.KeyboardEvent) {
    if (e.key !== "ArrowDown" && e.key !== "ArrowUp") return;
    e.preventDefault();
    const idx = selectedThreat ? visibleThreats.findIndex((t) => t.id === selectedThreat.id) : -1;
    const next =
      e.key === "ArrowDown"
        ? Math.min(idx + 1, visibleThreats.length - 1)
        : Math.max(idx - 1, 0);
    if (visibleThreats[next]) setSelectedThreat(visibleThreats[next]);
  }

  const openVisibleThreats = visibleThreats.filter((t) => t.status === "Open");

  async function handleBulkTriage(status: "Accepted" | "Mitigated" | "Rejected") {
    await Promise.all(openVisibleThreats.map((t) => updateStatus.mutateAsync({ threatId: t.id, status })));
    toast.success(`${openVisibleThreats.length} threat${openVisibleThreats.length !== 1 ? "s" : ""} marked as ${status}`);
  }

  const methodCategories = [...new Set(displayedAllThreats.map((t) => t.methodCategory))];
  const frameworks = [...new Set(displayedAllThreats.flatMap((t) => (t.frameworkMappings ?? []).map((m) => m.framework)))];

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
            <Button variant="outline" size="sm" onClick={() => setShowReanalyzeDialog(true)} className="gap-1.5">
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

        {/* Evidence gaps banner */}
        {(unansweredCount > 0 || reviewQuestions.length > 0) && (
          <div className="flex items-center gap-3 border-b bg-amber-50 px-4 py-2 text-xs">
            <HelpCircle className="h-3.5 w-3.5 shrink-0 text-amber-600" />
            <span className="text-amber-800 font-medium flex-1">
              {[
                unansweredCount > 0 && `${unansweredCount} unanswered pre-analysis question${unansweredCount !== 1 ? "s" : ""} — re-analyze to improve results`,
                reviewQuestions.length > 0 && `${reviewQuestions.length} open question${reviewQuestions.length !== 1 ? "s" : ""} from the analysis requiring your input`,
              ].filter(Boolean).join(" · ")}
            </span>
            {reviewQuestions.length > 0 && (
              <button
                onClick={() => {
                  const text = reviewQuestions.join("\n\n");
                  void navigator.clipboard.writeText(text);
                  toast.success("Questions copied");
                }}
                className="flex items-center gap-1 text-amber-700 hover:text-amber-900 transition-colors shrink-0"
              >
                <Copy className="h-3 w-3" />
                Copy questions
              </button>
            )}
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

        {/* Severity stats bar */}
        {displayedAllThreats.length > 0 && <SeverityBar threats={displayedAllThreats} />}

        {/* System summary + classification */}
        {analysis && (
          <div className="border-b px-4 py-2 shrink-0 space-y-1.5">
            {typeof analysis["systemSummary"] === "string" && (
              <p className="text-sm text-muted-foreground line-clamp-2">{analysis["systemSummary"]}</p>
            )}
            <div className="flex flex-wrap items-center gap-2">
              {Array.isArray(analysis["classification"]) &&
                (analysis["classification"] as string[]).map((c) => (
                  <Badge key={c} variant="outline" className="text-xs">{c}</Badge>
                ))}
              <Badge variant="secondary" className="text-xs">{displayedAllThreats.length} threats</Badge>
              <Badge variant="outline" className="text-xs">{rejectedCandidates.length} discarded</Badge>
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
          </div>
        )}

        {/* Tabs — ordered by priority */}
        <div className="min-h-0 flex-1 overflow-hidden">
          <Tabs value={activeTab} onValueChange={setActiveTab} className="flex h-full min-h-0 flex-col">
            <TabsList className="mx-4 mt-2 w-auto max-w-[calc(100vw-2rem)] shrink-0 justify-start overflow-x-auto whitespace-nowrap">
              {/* Primary — highlighted */}
              <TabsTrigger value="threats" className="data-[state=active]:font-semibold">
                Threats
                <span className={cn(
                  "ml-1.5 rounded-full px-1.5 py-0.5 text-[10px] font-semibold",
                  displayedAllThreats.some(t => t.riskRating?.severity === "critical")
                    ? "bg-red-100 text-red-700"
                    : "bg-muted text-muted-foreground"
                )}>
                  {elementIdFilter ? `${displayedThreats.length}/${displayedAllThreats.length}` : displayedAllThreats.length}
                </span>
              </TabsTrigger>
              <TabsTrigger value="architecture">Architecture</TabsTrigger>
              <TabsTrigger value="remediation">Remediation</TabsTrigger>
              <TabsTrigger value="recommendations">Recommendations</TabsTrigger>
              <TabsTrigger value="tests">
                Tests
                {(() => {
                  const count = Array.isArray(analysis?.["securityTestCases"]) ? (analysis!["securityTestCases"] as unknown[]).length : 0;
                  return count > 0 ? (
                    <span className="ml-1.5 rounded-full bg-muted px-1.5 py-0.5 text-[10px] text-muted-foreground">{count}</span>
                  ) : null;
                })()}
              </TabsTrigger>
              <TabsTrigger value="trees">
                Attack Trees
                {(() => {
                  const count = Array.isArray(analysis?.["attackTrees"]) ? (analysis!["attackTrees"] as unknown[]).length : 0;
                  return count > 0 ? (
                    <span className="ml-1.5 rounded-full bg-muted px-1.5 py-0.5 text-[10px] text-muted-foreground">{count}</span>
                  ) : null;
                })()}
              </TabsTrigger>
              <TabsTrigger value="discarded" className="text-muted-foreground">
                Discarded
                <span className="ml-1.5 rounded-full bg-muted px-1.5 py-0.5 text-[10px] text-muted-foreground">
                  {rejectedCandidates.length}
                </span>
              </TabsTrigger>
              <TabsTrigger value="export">Export</TabsTrigger>
            </TabsList>

            {/* ── Threats ── */}
            {activeTab === "threats" && (
              <TabsContent value="threats" className="mt-0 flex h-full min-h-0 flex-1 overflow-hidden">
                {/* Left: filter + list */}
                <div className="flex w-full shrink-0 flex-col gap-2 border-r p-3 md:h-full md:w-[52rem]">
                  {/* Search */}
                  <div className="relative">
                    <Search className="absolute left-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-muted-foreground pointer-events-none" />
                    <input
                      type="search"
                      placeholder="Search threats…"
                      value={searchQuery}
                      onChange={(e) => {
                        const next = new URLSearchParams(searchParams);
                        if (e.target.value) next.set("q", e.target.value);
                        else next.delete("q");
                        setSearchParams(next, { replace: true });
                      }}
                      className="w-full rounded-md border bg-transparent py-1.5 pl-8 pr-3 text-sm outline-none focus:ring-2 focus:ring-ring placeholder:text-muted-foreground"
                    />
                  </div>

                  {/* Filters + Open-only toggle */}
                  <div className="flex items-start gap-2">
                    <div className="flex-1 min-w-0">
                      <ThreatFilterBar
                        methodCategories={methodCategories}
                        frameworks={frameworks}
                        elementFilter={selectedElementForFilter
                          ? { id: selectedElementForFilter.id, name: selectedElementForFilter.name }
                          : undefined}
                        onClearElement={handleClearElementFilter}
                      />
                    </div>
                    <button
                      title="Show open threats only"
                      onClick={() => {
                        const next = new URLSearchParams(searchParams);
                        const isOpenOnly = statusFilters.length === 1 && statusFilters[0] === "Open";
                        next.delete("status");
                        if (!isOpenOnly) next.set("status", "Open");
                        setSearchParams(next);
                      }}
                      className={cn(
                        "mt-0.5 flex shrink-0 items-center gap-1 rounded-md border px-2.5 py-1.5 text-xs font-medium transition-colors",
                        statusFilters.length === 1 && statusFilters[0] === "Open"
                          ? "border-primary bg-primary/10 text-primary"
                          : "border-border text-muted-foreground hover:bg-muted hover:text-foreground",
                      )}
                    >
                      <span className="h-1.5 w-1.5 rounded-full bg-muted-foreground/40" />
                      Open only
                    </button>
                  </div>

                  {/* What's left summary */}
                  {(() => {
                    const openCritical = displayedAllThreats.filter((t) => t.status === "Open" && t.riskRating?.severity === "critical").length;
                    const openHigh = displayedAllThreats.filter((t) => t.status === "Open" && t.riskRating?.severity === "high").length;
                    if (openCritical === 0 && openHigh === 0) return null;
                    const parts = [
                      openCritical > 0 && `${openCritical} critical`,
                      openHigh > 0 && `${openHigh} high`,
                    ].filter(Boolean).join(" · ");
                    return (
                      <div className="flex items-center gap-2 rounded-md border border-red-200 bg-red-50 dark:border-red-900 dark:bg-red-950/30 px-3 py-2 text-xs">
                        <AlertTriangle className="h-3.5 w-3.5 shrink-0 text-red-600 dark:text-red-400" />
                        <span className="font-medium text-red-700 dark:text-red-300">{parts} open — prioritize these</span>
                      </div>
                    );
                  })()}

                  <div className="flex gap-2">
                    <Button variant="outline" size="sm" onClick={() => setShowAddThreat(true)} className="flex-1">
                      + Add threat
                    </Button>
                    {openVisibleThreats.length > 0 && (
                      <div className="flex gap-1">
                        <button
                          title={`Accept all ${openVisibleThreats.length} open threats`}
                          onClick={() => void handleBulkTriage("Accepted")}
                          disabled={updateStatus.isPending}
                          className="flex items-center gap-1 rounded-md border px-2.5 py-1.5 text-xs font-medium text-green-700 border-green-200 hover:bg-green-50 dark:text-green-400 dark:border-green-900 dark:hover:bg-green-950/40 transition-colors disabled:opacity-50"
                        >
                          <CheckCircle className="h-3.5 w-3.5" />
                          Accept all
                        </button>
                        <button
                          title={`Mitigate all ${openVisibleThreats.length} open threats`}
                          onClick={() => void handleBulkTriage("Mitigated")}
                          disabled={updateStatus.isPending}
                          className="flex items-center gap-1 rounded-md border px-2.5 py-1.5 text-xs font-medium text-blue-700 border-blue-200 hover:bg-blue-50 dark:text-blue-400 dark:border-blue-900 dark:hover:bg-blue-950/40 transition-colors disabled:opacity-50"
                        >
                          <ChevronsUpDown className="h-3.5 w-3.5" />
                          Mitigate all
                        </button>
                      </div>
                    )}
                  </div>

                  <div
                    ref={listRef}
                    tabIndex={0}
                    onKeyDown={handleListKeyDown}
                    className="min-h-0 flex-1 overflow-y-auto outline-none"
                  >
                    {threatsLoading ? (
                      <div className="space-y-1">
                        {[1, 2, 3].map((i) => <Skeleton key={i} className="h-9 w-full" />)}
                      </div>
                    ) : visibleThreats.length === 0 ? (
                      <p className="py-6 text-center text-sm text-muted-foreground">
                        {searchQuery ? "No threats match your search." : "No threats match your filters."}
                      </p>
                    ) : (
                      <div className="space-y-1">
                        {visibleThreats.map((t) => (
                          <ThreatCard
                            key={t.id}
                            threat={t}
                            selected={selectedThreat?.id === t.id}
                            onClick={setSelectedThreat}
                            onShowInArchitecture={handleShowThreatInArchitecture}
                            onUpdateStatus={async (id, status) => { await updateStatus.mutateAsync({ threatId: id, status }); }}
                          />
                        ))}
                      </div>
                    )}
                  </div>
                </div>

                {/* Right: threat detail OR summary panel */}
                {selectedThreat ? (
                  <div className="min-h-0 flex-1 overflow-hidden md:h-full">
                    <ThreatDetailPanel
                      threat={selectedThreat}
                      onClose={() => setSelectedThreat(null)}
                      onUpdateStatus={async (id, status) => { await updateStatus.mutateAsync({ threatId: id, status }); }}
                      onAddNote={async (id, body) => { await addNote.mutateAsync({ threatId: id, body }); }}
                      onShowInArchitecture={handleShowThreatInArchitecture}
                    />
                  </div>
                ) : (
                  <TopDriversPanel
                    threats={displayedAllThreats}
                    elements={architecture?.elements ?? []}
                    onElementClick={handleDriverClick}
                  />
                )}
              </TabsContent>
            )}

            {/* ── Tests ── */}
            {activeTab === "tests" && (
              <TabsContent value="tests" className="flex-1 overflow-y-auto mt-0">
                <TestCasesPanel
                  testCases={
                    Array.isArray(analysis?.["securityTestCases"])
                      ? (analysis!["securityTestCases"] as Array<{ threatIdentifier: string; threatTitle: string; scenarios: Array<{ scenarioTitle: string; given: string; when: string; then: string; and?: string | null }> }>)
                      : []
                  }
                  onThreatClick={(identifier) => {
                    const threat = displayedAllThreats.find((t) => t.identifier === identifier);
                    if (threat) { setSelectedThreat(threat); setActiveTab("threats"); }
                  }}
                />
              </TabsContent>
            )}

            {/* ── Attack Trees ── */}
            {activeTab === "trees" && (
              <TabsContent value="trees" className="flex-1 overflow-y-auto mt-0">
                <AttackTreesPanel
                  attackTrees={
                    Array.isArray(analysis?.["attackTrees"])
                      ? (analysis!["attackTrees"] as Array<{ threatIdentifier: string; threatTitle: string; mermaidDiagram: string; textSummary: string }>)
                      : []
                  }
                  onThreatClick={(identifier) => {
                    const threat = displayedAllThreats.find((t) => t.identifier === identifier);
                    if (threat) { setSelectedThreat(threat); setActiveTab("threats"); }
                  }}
                />
              </TabsContent>
            )}

            {/* ── Discarded ── */}
            {activeTab === "discarded" && (
              <TabsContent value="discarded" className="mt-0 flex h-full min-h-0 flex-1 flex-col overflow-hidden">
                <div className="border-b px-4 py-3 text-sm text-muted-foreground">
                  Threats rejected during analysis/synthesis. You can manually promote any discarded threat into your threat list.
                </div>
                <div className="min-h-0 flex-1 overflow-y-auto p-4">
                  {rejectedLoading ? (
                    <div className="space-y-2">{[1, 2, 3].map((i) => <Skeleton key={i} className="h-28 w-full" />)}</div>
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
                              onClick={() => { setDraftFromRejected(candidate); setShowAddThreat(true); }}
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

            {/* ── Architecture ── */}
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
                    {(selectedThreat ?? selectedElement) && (
                      <div className="min-h-0 w-full shrink-0 overflow-y-auto border-l xl:h-full xl:w-[24rem]">
                        {selectedThreat ? (
                          <ThreatDetailPanel
                            threat={selectedThreat}
                            onClose={() => setSelectedThreat(null)}
                            onUpdateStatus={async (id, status) => { await updateStatus.mutateAsync({ threatId: id, status }); }}
                            onAddNote={async (id, body) => { await addNote.mutateAsync({ threatId: id, body }); }}
                          />
                        ) : selectedElement ? (
                          <ElementDetailPanel
                            element={selectedElement}
                            readOnly
                            onPatch={async () => undefined}
                            onDelete={async () => undefined}
                            onCorrect={async () => undefined}
                            relatedThreats={threatsForSelectedElement}
                            onThreatClick={(t) => { setSelectedThreat(t); }}
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

            {/* ── Remediation ── */}
            {activeTab === "remediation" && (
              <TabsContent value="remediation" className="mt-0 flex h-full min-h-0 flex-1 flex-col overflow-hidden">
                <div className="border-b px-4 py-3 text-sm text-muted-foreground shrink-0">
                  Prioritized list of fixes across all confirmed threats, ordered by severity. Click a threat identifier to jump to the threat.
                </div>
                <div className="min-h-0 flex-1 overflow-y-auto">
                <RemediationPanel
                  items={
                    Array.isArray(analysis?.["prioritizedRemediationList"])
                      ? (analysis?.["prioritizedRemediationList"] as Array<{ threatIdentifier: string; title: string; mitigationSummary: string; priority: "critical" | "high" | "medium" | "low" }>)
                      : []
                  }
                  onThreatClick={(identifier) => {
                    const threat = displayedAllThreats.find((t) => t.identifier === identifier);
                    if (threat) { setSelectedThreat(threat); setActiveTab("threats"); }
                  }}
                />
                </div>
              </TabsContent>
            )}

            {/* ── Recommendations ── */}
            {activeTab === "recommendations" && (
              <TabsContent value="recommendations" className="mt-0 flex h-full min-h-0 flex-1 flex-col overflow-hidden">
                <div className="border-b px-4 py-3 text-sm text-muted-foreground shrink-0">
                  Secure-by-design recommendations that apply to the architecture as a whole, independent of specific threats.
                </div>
                <div className="min-h-0 flex-1 overflow-y-auto">
                  <RecommendationsPanel
                    recommendations={
                      Array.isArray(analysis?.["secureDesignRecommendations"])
                        ? (analysis?.["secureDesignRecommendations"] as Array<{ title: string; description: string; principles?: string[]; affectedElements?: string[]; relatedThreatIdentifiers?: string[] }>)
                        : []
                    }
                    onThreatClick={(identifier) => {
                      const threat = displayedAllThreats.find((t) => t.identifier === identifier);
                      if (threat) { setSelectedThreat(threat); setActiveTab("threats"); }
                    }}
                  />
                </div>
              </TabsContent>
            )}

            {/* ── Export ── */}
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
        onOpenChange={(open) => { setShowAddThreat(open); if (!open) setDraftFromRejected(null); }}
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
