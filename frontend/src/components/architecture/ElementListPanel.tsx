import { useState } from "react";
import { Search, PlusCircle } from "lucide-react";
import type { ArchitectureElement } from "@/api/architecture";
import { ELEMENT_TYPE_CONFIG } from "./elementTypeConfig";
import { ELEMENT_TYPES, type ElementType } from "@/lib/constants";
import { Input } from "@/components/ui/input";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";

interface ElementListPanelProps {
  elements: ArchitectureElement[];
  selectedElementId?: string | null | undefined;
  onElementSelect: (element: ArchitectureElement) => void;
  onAddElement?: () => void;
  readOnly?: boolean;
}

export function ElementListPanel({
  elements,
  selectedElementId,
  onElementSelect,
  onAddElement,
  readOnly = false,
}: ElementListPanelProps) {
  const [search, setSearch] = useState("");

  const filtered = search.trim()
    ? elements.filter(
        (e) =>
          e.name.toLowerCase().includes(search.toLowerCase()) ||
          e.elementType.toLowerCase().includes(search.toLowerCase()),
      )
    : elements;

  // Group by type
  const grouped = new Map<ElementType, ArchitectureElement[]>();
  ELEMENT_TYPES.forEach((t) => {
    const items = filtered.filter((e) => e.elementType === t);
    if (items.length > 0) grouped.set(t, items);
  });

  return (
    <div className="flex h-full flex-col gap-2 overflow-hidden">
      <div className="shrink-0 space-y-2 p-3">
        <div className="relative">
          <Search className="absolute left-2.5 top-2.5 h-4 w-4 text-muted-foreground" />
          <Input
            className="pl-8"
            placeholder="Search elements…"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
          />
        </div>
        {!readOnly && onAddElement && (
          <Button onClick={onAddElement} className="w-full" size="sm" variant="outline">
            <PlusCircle className="mr-2 h-4 w-4" />
            Add element
          </Button>
        )}
      </div>

      <div className="flex-1 overflow-y-auto px-2 pb-2">
        {grouped.size === 0 ? (
          <p className="p-4 text-center text-sm text-muted-foreground">
            {elements.length === 0 ? "No elements yet" : "No elements match your search"}
          </p>
        ) : (
          Array.from(grouped.entries()).map(([type, items]) => {
            const config = ELEMENT_TYPE_CONFIG[type];
            return (
              <div key={type} className="mb-3">
                <p className="mb-1 px-1 text-xs font-semibold uppercase tracking-wide text-muted-foreground">
                  {config.label}s ({items.length})
                </p>
                <div className="space-y-1">
                  {items.map((el) => (
                    <button
                      key={el.id}
                      onClick={() => onElementSelect(el)}
                      className={cn(
                        "flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-left text-sm transition-colors",
                        selectedElementId === el.id
                          ? "bg-primary/10 text-primary"
                          : "hover:bg-muted",
                      )}
                    >
                      <span>{config.icon}</span>
                      <span className="flex-1 truncate">{el.name}</span>
                      {el.source === "UserAdded" && (
                        <span className="shrink-0 rounded-full bg-purple-100 px-1.5 py-0.5 text-[10px] text-purple-700">
                          Added
                        </span>
                      )}
                    </button>
                  ))}
                </div>
              </div>
            );
          })
        )}
      </div>

      {/* Accessibility table fallback (screen-reader) */}
      <table className="sr-only" aria-label="Architecture elements">
        <thead>
          <tr>
            <th>Name</th>
            <th>Type</th>
            <th>Source</th>
            <th>Description</th>
          </tr>
        </thead>
        <tbody>
          {elements.map((el) => (
            <tr key={el.id}>
              <td>{el.name}</td>
              <td>{el.elementType}</td>
              <td>{el.source}</td>
              <td>{el.description ?? ""}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
