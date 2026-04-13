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
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { CORRECTION_TYPES, type CorrectionType } from "@/lib/constants";
import type { CorrectElementRequest } from "@/api/architecture";

const schema = z
  .object({
    correctionType: z.enum(CORRECTION_TYPES),
    fieldName: z.string().max(100).optional(),
    originalValue: z.string().max(2000).optional(),
    correctedValue: z.string().max(2000).optional(),
    note: z.string().max(2000).optional(),
  })
  .refine(
    (data) => {
      if (data.correctionType === "Update") return !!data.fieldName?.trim();
      return true;
    },
    { message: "Field name is required for Update corrections", path: ["fieldName"] },
  );

type FormValues = z.infer<typeof schema>;

interface AddCorrectionModalProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onSubmit: (req: CorrectElementRequest) => Promise<void>;
}

const CORRECTION_LABELS: Record<CorrectionType, string> = {
  Update: "Update a field value",
  MarkIncorrect: "Mark as incorrect",
  MarkAssumed: "Mark as assumed",
  MarkConfirmed: "Mark as confirmed",
  AddNote: "Add a note",
};

export function AddCorrectionModal({ open, onOpenChange, onSubmit }: AddCorrectionModalProps) {
  const {
    register,
    handleSubmit,
    setValue,
    watch,
    reset,
    formState: { errors, isSubmitting },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { correctionType: "Update" },
  });

  const correctionType = watch("correctionType");

  async function onFormSubmit(values: FormValues) {
    await onSubmit({
      correctionType: values.correctionType,
      fieldName: values.fieldName || undefined,
      originalValue: values.originalValue || undefined,
      correctedValue: values.correctedValue || undefined,
      note: values.note || undefined,
    });
    reset();
    onOpenChange(false);
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>Add correction</DialogTitle>
        </DialogHeader>

        <form onSubmit={handleSubmit(onFormSubmit)} className="space-y-4">
          <div className="space-y-1.5">
            <Label>Correction type</Label>
            <Select
              value={correctionType}
              onValueChange={(v) => setValue("correctionType", v as CorrectionType)}
            >
              <SelectTrigger>
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {CORRECTION_TYPES.map((t) => (
                  <SelectItem key={t} value={t}>
                    {CORRECTION_LABELS[t]}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          {correctionType === "Update" && (
            <>
              <div className="space-y-1.5">
                <Label htmlFor="fieldName">Field name *</Label>
                <Input
                  id="fieldName"
                  {...register("fieldName")}
                  placeholder="e.g. name, description, properties.port"
                />
                {errors.fieldName && (
                  <p className="text-sm text-destructive">{errors.fieldName.message}</p>
                )}
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="originalValue">Original value</Label>
                <Input
                  id="originalValue"
                  {...register("originalValue")}
                  placeholder="What was extracted"
                />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="correctedValue">Corrected value</Label>
                <Input
                  id="correctedValue"
                  {...register("correctedValue")}
                  placeholder="What it should be"
                />
              </div>
            </>
          )}

          {correctionType === "AddNote" && (
            <div className="space-y-1.5">
              <Label htmlFor="note">Note</Label>
              <Textarea id="note" {...register("note")} rows={4} placeholder="Your note…" />
            </div>
          )}

          <DialogFooter>
            <Button
              type="button"
              variant="outline"
              onClick={() => onOpenChange(false)}
              disabled={isSubmitting}
            >
              Cancel
            </Button>
            <Button type="submit" disabled={isSubmitting}>
              {isSubmitting ? "Saving…" : "Save correction"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
