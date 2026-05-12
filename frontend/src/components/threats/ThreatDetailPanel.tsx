import { useState } from "react";
import { formatDistanceToNow } from "date-fns";
import { X, MessageSquare, Shield, Activity, CheckSquare } from "lucide-react";
import type { Threat } from "@/api/threats";
import { ThreatStatusBadge } from "./ThreatStatusBadge";
import { FindingTypeBadge } from "./FindingTypeBadge";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/textarea";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Separator } from "@/components/ui/separator";
import type { ThreatStatus } from "@/lib/constants";
import { THREAT_STATUSES } from "@/lib/constants";
import { toast } from "sonner";

interface ThreatDetailPanelProps {
  threat: Threat;
  onClose: () => void;
  onUpdateStatus: (threatId: string, status: ThreatStatus) => Promise<void>;
  onAddNote: (threatId: string, body: string) => Promise<void>;
  onShowInArchitecture?: (threat: Threat) => void;
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div className="space-y-1">
      <p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">{title}</p>
      {children}
    </div>
  );
}

export function ThreatDetailPanel({
  threat,
  onClose,
  onUpdateStatus,
  onAddNote,
  onShowInArchitecture,
}: ThreatDetailPanelProps) {
  const impactedAssets = Array.isArray(threat.impactedAssets) ? threat.impactedAssets : [];
  const mitigations = Array.isArray(threat.mitigations) ? threat.mitigations : [];
  const frameworkMappings = Array.isArray(threat.frameworkMappings) ? threat.frameworkMappings : [];
  const notes = Array.isArray(threat.notes) ? threat.notes : [];
  const sourceMethods = Array.isArray(threat.sourceMethods) ? threat.sourceMethods : [];

  const [newStatus, setNewStatus] = useState<ThreatStatus>(threat.status);
  const [noteBody, setNoteBody] = useState("");
  const [isUpdatingStatus, setIsUpdatingStatus] = useState(false);
  const [isAddingNote, setIsAddingNote] = useState(false);

  async function handleStatusUpdate() {
    if (newStatus === threat.status) return;
    setIsUpdatingStatus(true);
    try {
      await onUpdateStatus(threat.id, newStatus);
      toast.success("Status updated");
    } catch {
      toast.error("Failed to update status");
    } finally {
      setIsUpdatingStatus(false);
    }
  }

  async function handleAddNote() {
    if (!noteBody.trim()) return;
    setIsAddingNote(true);
    try {
      await onAddNote(threat.id, noteBody.trim());
      setNoteBody("");
      toast.success("Note added");
    } catch {
      toast.error("Failed to add note");
    } finally {
      setIsAddingNote(false);
    }
  }

  return (
    <div className="flex h-full flex-col">
      {/* Header */}
      <div className="flex items-start gap-2 border-b p-4">
        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-2 flex-wrap">
            <span className="rounded bg-muted px-1.5 py-0.5 text-xs font-mono font-bold">
              {threat.identifier}
            </span>
            <FindingTypeBadge findingType={threat.findingType} className="text-xs" />
          </div>
          <h3 className="mt-1 font-semibold leading-snug">{threat.title}</h3>
        </div>
        <div className="flex shrink-0 items-center gap-1">
          {onShowInArchitecture && threat.affectedElementIds.length > 0 && (
            <Button
              type="button"
              size="sm"
              variant="outline"
              className="h-7 text-xs"
              onClick={() => onShowInArchitecture(threat)}
            >
              Show in architecture
            </Button>
          )}
          <button onClick={onClose} className="text-muted-foreground hover:text-foreground" aria-label="Close">
            <X className="h-5 w-5" />
          </button>
        </div>
      </div>

      {/* Scrollable content */}
      <div className="flex-1 overflow-y-auto p-4 space-y-4">
        <div className="flex flex-wrap gap-2">
          <ThreatStatusBadge status={threat.status} />
          {threat.riskRating && (
            <Badge
              variant={
                threat.riskRating.severity === "critical" || threat.riskRating.severity === "high"
                  ? "destructive"
                  : threat.riskRating.severity === "medium"
                  ? "warning"
                  : "secondary"
              }
              className="font-semibold"
            >
              {threat.riskRating.severity.charAt(0).toUpperCase() + threat.riskRating.severity.slice(1)}
            </Badge>
          )}
          <Badge variant="outline">{threat.methodCategory}</Badge>
          {sourceMethods.map((method) => (
            <Badge key={method} variant="secondary">{method}</Badge>
          ))}
          <Badge variant={threat.confidence === "High" ? "success" : threat.confidence === "Medium" ? "warning" : "destructive"}>
            {threat.confidence} confidence
          </Badge>
        </div>

        {threat.riskRating && (
          <Section title="OWASP Risk Rating">
            <div className="rounded-md border p-3 space-y-2">
              <div className="flex items-center gap-2 text-sm">
                <Activity className="h-3.5 w-3.5 text-muted-foreground" />
                <span className="text-muted-foreground">Likelihood:</span>
                <span className="font-medium capitalize">{threat.riskRating.likelihood}</span>
                <span className="mx-1 text-muted-foreground">·</span>
                <span className="text-muted-foreground">Impact:</span>
                <span className="font-medium capitalize">{threat.riskRating.impact}</span>
              </div>
              {threat.riskRating.likelihoodJustification && (
                <p className="text-xs text-muted-foreground">
                  <span className="font-medium">Likelihood: </span>
                  {threat.riskRating.likelihoodJustification}
                </p>
              )}
              {threat.riskRating.impactJustification && (
                <p className="text-xs text-muted-foreground">
                  <span className="font-medium">Impact: </span>
                  {threat.riskRating.impactJustification}
                </p>
              )}
            </div>
          </Section>
        )}

        <Section title="Description">
          <p className="text-sm">{threat.description}</p>
        </Section>

        <Section title="Attack scenario">
          <p className="text-sm">{threat.attackScenario}</p>
        </Section>

        {threat.preconditions && (
          <Section title="Preconditions">
            <p className="text-sm">{threat.preconditions}</p>
          </Section>
        )}

        {impactedAssets.length > 0 && (
          <Section title="Impacted assets">
            <div className="flex flex-wrap gap-1">
              {impactedAssets.map((a) => <Badge key={a} variant="outline" className="text-xs">{a}</Badge>)}
            </div>
          </Section>
        )}

        {threat.securityImpact && (
          <Section title="Security impact"><p className="text-sm">{threat.securityImpact}</p></Section>
        )}
        {threat.privacyImpact && (
          <Section title="Privacy impact"><p className="text-sm">{threat.privacyImpact}</p></Section>
        )}
        {threat.existingControls && (
          <Section title="Existing controls"><p className="text-sm">{threat.existingControls}</p></Section>
        )}
        {threat.controlGaps && (
          <Section title="Control gaps"><p className="text-sm">{threat.controlGaps}</p></Section>
        )}

        {/* Mitigations */}
        {mitigations.length > 0 && (
          <Section title="Mitigations">
            <div className="space-y-2">
              {mitigations.map((m) => (
                <div key={m.id} className="rounded-md border p-3 space-y-1.5">
                  <div className="flex items-center gap-2">
                    <Shield className="h-3.5 w-3.5 text-green-600" />
                    <span className="text-sm font-medium">{m.title}</span>
                    <Badge variant={m.priority === "critical" || m.priority === "high" ? "destructive" : m.priority === "medium" ? "warning" : "secondary"} className="ml-auto text-xs capitalize">
                      {m.priority}
                    </Badge>
                  </div>
                  <p className="text-xs text-muted-foreground">{m.description}</p>
                  {m.acceptanceCriteria?.length > 0 && (
                    <div className="space-y-0.5 pt-0.5">
                      <p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">Done when</p>
                      {m.acceptanceCriteria.map((ac, i) => (
                        <div key={i} className="flex items-start gap-1.5">
                          <CheckSquare className="mt-0.5 h-3 w-3 shrink-0 text-muted-foreground" />
                          <span className="text-xs text-muted-foreground">{ac}</span>
                        </div>
                      ))}
                    </div>
                  )}
                </div>
              ))}
            </div>
          </Section>
        )}

        {/* Framework mappings */}
        {frameworkMappings.length > 0 && (
          <Section title="Framework mappings">
            <div className="space-y-1">
              {frameworkMappings.map((fm, idx) => (
                <div key={idx} className="flex items-center gap-2 text-xs">
                  <Badge variant="outline">{fm.framework}</Badge>
                  <span className="font-mono">{fm.reference}</span>
                  <span className="text-muted-foreground capitalize">({fm.mappingType})</span>
                </div>
              ))}
            </div>
          </Section>
        )}

        <Separator />

        {/* Status update */}
        <Section title="Update status">
          <div className="flex items-center gap-2">
            <Select value={newStatus} onValueChange={(v) => setNewStatus(v as ThreatStatus)}>
              <SelectTrigger className="flex-1">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {THREAT_STATUSES.map((s) => (
                  <SelectItem key={s} value={s}>{s}</SelectItem>
                ))}
              </SelectContent>
            </Select>
            <Button
              size="sm"
              onClick={handleStatusUpdate}
              disabled={newStatus === threat.status || isUpdatingStatus}
            >
              {isUpdatingStatus ? "Saving…" : "Save"}
            </Button>
          </div>
        </Section>

        {/* Notes */}
        <Section title={`Notes (${notes.length})`}>
          <div className="space-y-2">
            {notes.map((note) => (
              <div key={note.id} className="rounded-md bg-muted/50 p-3">
                <p className="text-sm">{note.body}</p>
                <p className="mt-1 text-xs text-muted-foreground">
                  {formatDistanceToNow(new Date(note.createdAt), { addSuffix: true })}
                </p>
              </div>
            ))}

            <div className="space-y-1.5">
              <Label htmlFor="note-body" className="text-xs">Add note</Label>
              <Textarea
                id="note-body"
                value={noteBody}
                onChange={(e) => setNoteBody(e.target.value)}
                rows={3}
                placeholder="Add a note…"
              />
              <Button
                size="sm"
                variant="outline"
                className="gap-1"
                onClick={handleAddNote}
                disabled={!noteBody.trim() || isAddingNote}
              >
                <MessageSquare className="h-3.5 w-3.5" />
                {isAddingNote ? "Adding…" : "Add note"}
              </Button>
            </div>
          </div>
        </Section>
      </div>
    </div>
  );
}
