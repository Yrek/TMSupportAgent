import { useState } from "react";
import { formatDistanceToNow } from "date-fns";
import { Pencil, Trash2, PlusCircle, ChevronDown, ChevronRight, ShieldAlert } from "lucide-react";
import type { ArchitectureElement, CorrectElementRequest, PatchElementRequest } from "@/api/architecture";
import type { Threat } from "@/api/threats";
import { ELEMENT_TYPE_CONFIG } from "./elementTypeConfig";
import { AddCorrectionModal } from "./AddCorrectionModal";
import { ConfirmDialog } from "@/components/common/ConfirmDialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Label } from "@/components/ui/label";
import { Badge } from "@/components/ui/badge";
import { Separator } from "@/components/ui/separator";

const STATUS_VARIANT: Record<string, "secondary" | "destructive" | "outline"> = {
  Open: "destructive",
  Accepted: "secondary",
  Mitigated: "outline",
  Rejected: "secondary",
};

const CORRECTION_TYPE_LABELS: Record<string, string> = {
  Update: "Updated",
  MarkIncorrect: "Marked Incorrect",
  MarkAssumed: "Marked Assumed",
  MarkConfirmed: "Marked Confirmed",
  AddNote: "Note",
};

interface ElementDetailPanelProps {
  element: ArchitectureElement;
  readOnly?: boolean;
  onPatch: (req: PatchElementRequest) => Promise<void>;
  onDelete: () => Promise<void>;
  onCorrect: (req: CorrectElementRequest) => Promise<void>;
  onSoftRemove?: (() => Promise<void>) | undefined;
  /** GAP-TH5: related threats for this element shown when in analysis context */
  relatedThreats?: Threat[] | undefined;
  /** Called when user clicks a threat in the related-threats list */
  onThreatClick?: (threat: Threat) => void;
}

export function ElementDetailPanel({
  element,
  readOnly = false,
  onPatch,
  onDelete,
  onCorrect,
  onSoftRemove,
  relatedThreats,
  onThreatClick,
}: ElementDetailPanelProps) {
  const [editing, setEditing] = useState(false);
  const [name, setName] = useState(element.name);
  const [description, setDescription] = useState(element.description ?? "");
  const [isSaving, setIsSaving] = useState(false);
  const [showDeleteDialog, setShowDeleteDialog] = useState(false);
  const [showCorrectionModal, setShowCorrectionModal] = useState(false);
  const [correctionsExpanded, setCorrectionsExpanded] = useState(false);

  const config = ELEMENT_TYPE_CONFIG[element.elementType];
  const canDelete = element.source === "UserAdded";
  const canSoftRemove = element.source === "Extracted";

  async function handleSave() {
    setIsSaving(true);
    try {
      await onPatch({ name: name || undefined, description: description || undefined });
      setEditing(false);
    } finally {
      setIsSaving(false);
    }
  }

  async function handleDelete() {
    await onDelete();
  }

  const knownPropKeys = ["port", "protocol", "auth", "trustZone", "technology", "encryption", "from", "to"];

  return (
    <div className="h-full overflow-y-auto p-4 space-y-4">
      {/* Header */}
      <div className="flex items-start justify-between gap-2">
        <div className="flex items-center gap-2">
          <span className="text-xl">{config.icon}</span>
          <div>
            <h3 className="font-semibold">{element.name}</h3>
            <p className={`text-xs font-medium ${config.textClass}`}>{config.label}</p>
          </div>
        </div>
        {!readOnly && (
          <div className="flex items-center gap-1">
            <Button
              variant="ghost"
              size="icon"
              onClick={() => setEditing(!editing)}
              aria-label={editing ? "Cancel edit" : "Edit element"}
            >
              <Pencil className="h-4 w-4" />
            </Button>
            {canDelete && (
              <Button
                variant="ghost"
                size="icon"
                onClick={() => setShowDeleteDialog(true)}
                aria-label="Delete element"
                className="text-destructive hover:text-destructive"
              >
                <Trash2 className="h-4 w-4" />
              </Button>
            )}
            {canSoftRemove && onSoftRemove && (
              <Button
                variant="ghost"
                size="icon"
                onClick={() => void onSoftRemove()}
                aria-label="Soft remove element"
                title="Soft remove (excluded from analysis)"
                className="text-destructive hover:text-destructive"
              >
                <Trash2 className="h-4 w-4" />
              </Button>
            )}
          </div>
        )}
      </div>

      {/* Source + confidence */}
      <div className="flex items-center gap-2">
        <Badge variant={element.source === "Extracted" ? "info" : "purple"}>
          {element.source === "Extracted" ? "Extracted" : "User Added"}
        </Badge>
        {element.extractionConfidence && (
          <Badge variant="outline">Confidence: {element.extractionConfidence}</Badge>
        )}
      </div>

      <Separator />

      {/* Edit / view fields */}
      {editing ? (
        <div className="space-y-3">
          <div className="space-y-1.5">
            <Label htmlFor="el-name">Name</Label>
            <Input
              id="el-name"
              value={name}
              onChange={(e) => setName(e.target.value)}
              maxLength={255}
            />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="el-desc">Description</Label>
            <Textarea
              id="el-desc"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              rows={3}
            />
          </div>
          <div className="flex gap-2">
            <Button onClick={handleSave} disabled={isSaving} size="sm">
              {isSaving ? "Saving…" : "Save"}
            </Button>
            <Button variant="outline" size="sm" onClick={() => setEditing(false)}>
              Cancel
            </Button>
          </div>
        </div>
      ) : (
        element.description && (
          <p className="text-sm text-muted-foreground">{element.description}</p>
        )
      )}

      {/* Properties */}
      {Object.keys(element.properties).length > 0 && (
        <div className="space-y-2">
          <p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
            Properties
          </p>
          <div className="space-y-1">
            {knownPropKeys
              .filter((k) => element.properties[k] !== undefined)
              .map((k) => (
                <div key={k} className="flex gap-2 text-sm">
                  <span className="w-28 shrink-0 font-medium text-muted-foreground">{k}</span>
                  <span className="truncate">{String(element.properties[k])}</span>
                </div>
              ))}
            {Object.keys(element.properties)
              .filter((k) => !knownPropKeys.includes(k))
              .map((k) => (
                <div key={k} className="flex gap-2 text-sm">
                  <span className="w-28 shrink-0 font-medium text-muted-foreground">{k}</span>
                  <span className="truncate">{String(element.properties[k])}</span>
                </div>
              ))}
          </div>
        </div>
      )}

      {/* Corrections (extracted elements only) */}
      {element.source === "Extracted" && (
        <>
          <Separator />
          <div>
            <button
              onClick={() => setCorrectionsExpanded(!correctionsExpanded)}
              className="flex w-full items-center justify-between text-sm font-semibold"
            >
              <span>Corrections ({element.corrections.length})</span>
              {correctionsExpanded ? (
                <ChevronDown className="h-4 w-4" />
              ) : (
                <ChevronRight className="h-4 w-4" />
              )}
            </button>

            {correctionsExpanded && (
              <div className="mt-3 space-y-3">
                {element.corrections.map((c) => (
                  <div key={c.id} className="rounded-md border p-3 text-xs space-y-1">
                    <div className="flex items-center justify-between">
                      <Badge variant="secondary">
                        {CORRECTION_TYPE_LABELS[c.correctionType] ?? c.correctionType}
                      </Badge>
                      <span className="text-muted-foreground">
                        {formatDistanceToNow(new Date(c.createdAt), { addSuffix: true })}
                      </span>
                    </div>
                    {c.fieldName && (
                      <p>
                        <span className="font-medium">Field:</span> {c.fieldName}
                      </p>
                    )}
                    {c.originalValue && (
                      <p>
                        <span className="font-medium">Was:</span> {c.originalValue}
                      </p>
                    )}
                    {c.correctedValue && (
                      <p>
                        <span className="font-medium">Now:</span> {c.correctedValue}
                      </p>
                    )}
                    {c.note && <p className="text-muted-foreground">{c.note}</p>}
                  </div>
                ))}

                {!readOnly && (
                  <Button
                    variant="outline"
                    size="sm"
                    className="w-full gap-1"
                    onClick={() => setShowCorrectionModal(true)}
                  >
                    <PlusCircle className="h-4 w-4" />
                    Add correction
                  </Button>
                )}
              </div>
            )}

            {!correctionsExpanded && !readOnly && (
              <Button
                variant="ghost"
                size="sm"
                className="mt-2 gap-1 text-xs"
                onClick={() => {
                  setCorrectionsExpanded(true);
                  setShowCorrectionModal(true);
                }}
              >
                <PlusCircle className="h-3 w-3" />
                Add correction
              </Button>
            )}
          </div>
        </>
      )}

      {/* GAP-TH5: related threats — shown in analysis context */}
      {relatedThreats !== undefined && (
        <>
          <Separator />
          <div className="space-y-2">
            <div className="flex items-center gap-2">
              <ShieldAlert className="h-4 w-4 text-muted-foreground" />
              <p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                Threats ({relatedThreats.length})
              </p>
            </div>
            {relatedThreats.length === 0 ? (
              <p className="text-xs text-muted-foreground">No threats mapped to this element.</p>
            ) : (
              <div className="space-y-1.5">
                {relatedThreats.map((t) => (
                  <button
                    key={t.id}
                    onClick={() => onThreatClick?.(t)}
                    className="w-full rounded-md border p-2 text-left text-xs hover:bg-muted/50 transition-colors"
                  >
                    <div className="flex items-center justify-between gap-2">
                      <span className="font-mono text-muted-foreground shrink-0">{t.identifier}</span>
                      <Badge variant={STATUS_VARIANT[t.status] ?? "outline"} className="text-xs shrink-0">
                        {t.status}
                      </Badge>
                    </div>
                    <p className="mt-0.5 font-medium line-clamp-2">{t.title}</p>
                  </button>
                ))}
              </div>
            )}
          </div>
        </>
      )}

      <AddCorrectionModal
        open={showCorrectionModal}
        onOpenChange={setShowCorrectionModal}
        onSubmit={onCorrect}
      />

      <ConfirmDialog
        open={showDeleteDialog}
        onOpenChange={setShowDeleteDialog}
        title="Delete element"
        description={`Delete "${element.name}"? This cannot be undone.`}
        confirmLabel="Delete"
        confirmVariant="destructive"
        onConfirm={handleDelete}
      />
    </div>
  );
}
