import { useState } from "react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { PlusCircle, Trash2 } from "lucide-react";
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
import { ELEMENT_TYPES } from "@/lib/constants";
import { ELEMENT_TYPE_CONFIG } from "./elementTypeConfig";
import type { AddElementRequest } from "@/api/architecture";

const schema = z.object({
  elementType: z.enum(ELEMENT_TYPES),
  name: z.string().min(1, "Name is required").max(255),
  description: z.string().max(2000).optional(),
});

type FormValues = z.infer<typeof schema>;

interface AddElementModalProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  onSubmit: (req: AddElementRequest) => Promise<void>;
}

export function AddElementModal({ open, onOpenChange, onSubmit }: AddElementModalProps) {
  const [extraProps, setExtraProps] = useState<Array<{ key: string; value: string }>>([]);
  const [isSubmitting, setIsSubmitting] = useState(false);

  const {
    register,
    handleSubmit,
    setValue,
    watch,
    reset,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { elementType: "Component" },
  });

  const selectedType = watch("elementType");

  async function onFormSubmit(values: FormValues) {
    setIsSubmitting(true);
    try {
      const properties: Record<string, unknown> = {};
      extraProps.forEach(({ key, value }) => {
        if (key.trim()) properties[key.trim()] = value;
      });

      await onSubmit({
        elementType: values.elementType,
        name: values.name,
        description: values.description || undefined,
        properties: Object.keys(properties).length > 0 ? properties : undefined,
      });

      reset();
      setExtraProps([]);
      onOpenChange(false);
    } finally {
      setIsSubmitting(false);
    }
  }

  function addProp() {
    setExtraProps((prev) => [...prev, { key: "", value: "" }]);
  }

  function removeProp(idx: number) {
    setExtraProps((prev) => prev.filter((_, i) => i !== idx));
  }

  function updateProp(idx: number, field: "key" | "value", val: string) {
    setExtraProps((prev) => prev.map((p, i) => (i === idx ? { ...p, [field]: val } : p)));
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-lg max-h-[90vh] overflow-y-auto">
        <DialogHeader>
          <DialogTitle>Add element</DialogTitle>
        </DialogHeader>

        <form onSubmit={handleSubmit(onFormSubmit)} className="space-y-4">
          <div className="space-y-1.5">
            <Label htmlFor="elementType">Type</Label>
            <Select
              value={selectedType}
              onValueChange={(v) => setValue("elementType", v as (typeof ELEMENT_TYPES)[number])}
            >
              <SelectTrigger id="elementType">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {ELEMENT_TYPES.map((t) => {
                  const cfg = ELEMENT_TYPE_CONFIG[t];
                  return (
                    <SelectItem key={t} value={t}>
                      {cfg.icon} {cfg.label}
                    </SelectItem>
                  );
                })}
              </SelectContent>
            </Select>
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="name">Name *</Label>
            <Input id="name" {...register("name")} placeholder="e.g. Auth Service" />
            {errors.name && <p className="text-sm text-destructive">{errors.name.message}</p>}
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="description">Description</Label>
            <Textarea
              id="description"
              {...register("description")}
              placeholder="Brief description of this element…"
              rows={3}
            />
          </div>

          {/* Known properties for DataFlow */}
          {selectedType === "DataFlow" && (
            <div className="grid grid-cols-2 gap-3">
              <div className="space-y-1.5">
                <Label>From (element name)</Label>
                <Input
                  placeholder="Source element"
                  onChange={(e) => {
                    const idx = extraProps.findIndex((p) => p.key === "from");
                    if (idx >= 0) updateProp(idx, "value", e.target.value);
                    else setExtraProps((prev) => [...prev, { key: "from", value: e.target.value }]);
                  }}
                />
              </div>
              <div className="space-y-1.5">
                <Label>To (element name)</Label>
                <Input
                  placeholder="Target element"
                  onChange={(e) => {
                    const idx = extraProps.findIndex((p) => p.key === "to");
                    if (idx >= 0) updateProp(idx, "value", e.target.value);
                    else setExtraProps((prev) => [...prev, { key: "to", value: e.target.value }]);
                  }}
                />
              </div>
            </div>
          )}

          {/* Additional properties */}
          {extraProps
            .filter((p) => p.key !== "from" && p.key !== "to")
            .map((prop, idx) => (
              <div key={idx} className="flex items-center gap-2">
                <Input
                  placeholder="Property name"
                  value={prop.key}
                  onChange={(e) => updateProp(idx, "key", e.target.value)}
                  className="flex-1"
                />
                <Input
                  placeholder="Value"
                  value={prop.value}
                  onChange={(e) => updateProp(idx, "value", e.target.value)}
                  className="flex-1"
                />
                <Button
                  type="button"
                  variant="ghost"
                  size="icon"
                  onClick={() => removeProp(idx)}
                  aria-label="Remove property"
                >
                  <Trash2 className="h-4 w-4" />
                </Button>
              </div>
            ))}

          <Button type="button" variant="ghost" size="sm" onClick={addProp} className="gap-1">
            <PlusCircle className="h-4 w-4" />
            Add property
          </Button>

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
              {isSubmitting ? "Adding…" : "Add element"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
