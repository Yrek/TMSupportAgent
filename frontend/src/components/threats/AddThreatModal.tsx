import { useEffect } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import { Label } from "@/components/ui/label";
import { Badge } from "@/components/ui/badge";
import { ELEMENT_TYPE_CONFIG } from "@/components/architecture/elementTypeConfig";
import type { AddThreatRequest } from "@/api/threats";
import type { ArchitectureElement } from "@/api/architecture";

const schema = z.object({
  title: z.string().min(1, "Title is required").max(500),
  methodCategory: z.string().min(1, "Method category is required").max(100),
  description: z.string().min(1, "Description is required").max(5000),
  attackScenario: z.string().min(1, "Attack scenario is required").max(5000),
  // GAP-TH1: at least one element required (spec data-model §9)
  affectedElementIds: z.array(z.string()).min(1, "Select at least one affected element"),
  preconditions: z.string().max(2000).optional(),
  securityImpact: z.string().max(2000).optional(),
  privacyImpact: z.string().max(2000).optional(),
});

type FormValues = z.infer<typeof schema>;

interface AddThreatModalProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onSubmit: (req: AddThreatRequest) => Promise<void>;
  elements?: ArchitectureElement[] | undefined;
  /** Pre-select a specific element (e.g. from canvas click) */
  preselectedElementId?: string | undefined;
  initialValues?: Partial<Pick<FormValues, "title" | "methodCategory" | "description" | "attackScenario" | "preconditions" | "securityImpact" | "privacyImpact">> | undefined;
}

export function AddThreatModal({
  open,
  onOpenChange,
  onSubmit,
  elements = [],
  preselectedElementId,
  initialValues,
}: AddThreatModalProps) {
  // DataFlow elements are edges, not selectable threat targets
  const selectableElements = elements.filter((e) => e.elementType !== "DataFlow");
  const validPreselectedElementId =
    preselectedElementId && selectableElements.some((e) => e.id === preselectedElementId)
      ? preselectedElementId
      : undefined;

  const {
    register,
    handleSubmit,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: {
      affectedElementIds: validPreselectedElementId ? [validPreselectedElementId] : [],
    },
  });

  useEffect(() => {
    if (!open) return;
    reset({
      title: initialValues?.title ?? "",
      methodCategory: initialValues?.methodCategory ?? "",
      description: initialValues?.description ?? "",
      attackScenario: initialValues?.attackScenario ?? "",
      affectedElementIds: validPreselectedElementId ? [validPreselectedElementId] : [],
      preconditions: initialValues?.preconditions ?? "",
      securityImpact: initialValues?.securityImpact ?? "",
      privacyImpact: initialValues?.privacyImpact ?? "",
    });
  }, [open, validPreselectedElementId, initialValues, reset]);

  async function onFormSubmit(values: FormValues) {
    await onSubmit({
      title: values.title,
      methodCategory: values.methodCategory,
      description: values.description,
      attackScenario: values.attackScenario,
      affectedElementIds: values.affectedElementIds,
      preconditions: values.preconditions || undefined,
      securityImpact: values.securityImpact || undefined,
      privacyImpact: values.privacyImpact || undefined,
    });
    reset();
    onOpenChange(false);
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-lg max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Add threat</DialogTitle>
        </DialogHeader>

        <form onSubmit={handleSubmit(onFormSubmit)} className="space-y-4">
          <div className="space-y-1.5">
            <Label htmlFor="title">Title *</Label>
            <Input id="title" {...register("title")} placeholder="Threat title…" />
            {errors.title && <p className="text-sm text-destructive">{errors.title.message}</p>}
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="methodCategory">Method category *</Label>
            <Input id="methodCategory" {...register("methodCategory")} placeholder="e.g. STRIDE, PASTA, Custom" />
            {errors.methodCategory && <p className="text-sm text-destructive">{errors.methodCategory.message}</p>}
          </div>

          {/* GAP-TH1: element multi-select — required, min 1 */}
          {selectableElements.length > 0 && (
            <div className="space-y-1.5">
              <Label>Affected elements *</Label>
              <p className="text-xs text-muted-foreground">
                Select all elements this threat applies to.
              </p>
              <div className="rounded-md border divide-y max-h-44 overflow-y-auto">
                {selectableElements.map((el) => {
                  const cfg = ELEMENT_TYPE_CONFIG[el.elementType];
                  return (
                    <label
                      key={el.id}
                      className="flex items-center gap-3 px-3 py-2 text-sm cursor-pointer hover:bg-muted/50"
                    >
                      <input
                        type="checkbox"
                        value={el.id}
                        {...register("affectedElementIds")}
                        className="accent-primary"
                      />
                      <span className="text-base leading-none">{cfg.icon}</span>
                      <span className="flex-1 truncate">{el.name}</span>
                      <Badge variant="outline" className="text-xs shrink-0">
                        {cfg.label}
                      </Badge>
                    </label>
                  );
                })}
              </div>
              {errors.affectedElementIds && (
                <p className="text-sm text-destructive">{errors.affectedElementIds.message}</p>
              )}
            </div>
          )}

          {selectableElements.length === 0 && (
            <div className="rounded-md border border-amber-200 bg-amber-50 p-3 text-sm text-amber-800">
              No architecture elements available. Add elements to the architecture before adding a threat.
            </div>
          )}

          <div className="space-y-1.5">
            <Label htmlFor="description">Description *</Label>
            <Textarea id="description" {...register("description")} rows={3} placeholder="Describe the threat…" />
            {errors.description && <p className="text-sm text-destructive">{errors.description.message}</p>}
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="attackScenario">Attack scenario *</Label>
            <Textarea id="attackScenario" {...register("attackScenario")} rows={3} placeholder="How might an attacker exploit this?" />
            {errors.attackScenario && <p className="text-sm text-destructive">{errors.attackScenario.message}</p>}
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="preconditions">Preconditions</Label>
            <Textarea id="preconditions" {...register("preconditions")} rows={2} placeholder="What must be true for this attack to succeed?" />
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-1.5">
              <Label htmlFor="securityImpact">Security impact</Label>
              <Textarea id="securityImpact" {...register("securityImpact")} rows={2} />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="privacyImpact">Privacy impact</Label>
              <Textarea id="privacyImpact" {...register("privacyImpact")} rows={2} />
            </div>
          </div>

          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => onOpenChange(false)} disabled={isSubmitting}>
              Cancel
            </Button>
            <Button type="submit" disabled={isSubmitting || selectableElements.length === 0}>
              {isSubmitting ? "Adding…" : "Add threat"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
