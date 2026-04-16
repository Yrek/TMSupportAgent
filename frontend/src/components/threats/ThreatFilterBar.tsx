import { useSearchParams } from "react-router-dom";
import { Badge } from "@/components/ui/badge";
import { cn } from "@/lib/utils";

type FilterSection<T extends string> = {
  label: string;
  param: string;
  values: Array<{ label: string; value: T }>;
};

const FINDING_TYPES: FilterSection<string>["values"] = [
  { label: "Confirmed", value: "Confirmed" },
  { label: "Conditional", value: "Conditional" },
  { label: "User Added", value: "UserAdded" },
];

const STATUSES: FilterSection<string>["values"] = [
  { label: "Open", value: "Open" },
  { label: "Accepted", value: "Accepted" },
  { label: "Mitigated", value: "Mitigated" },
  { label: "Rejected", value: "Rejected" },
];

const CONFIDENCES: FilterSection<string>["values"] = [
  { label: "High", value: "High" },
  { label: "Medium", value: "Medium" },
  { label: "Low", value: "Low" },
];

interface ThreatFilterBarProps {
  methodCategories?: string[];
  frameworks?: string[];
  /** GAP-TH3: active element filter set by canvas click */
  elementFilter?: { id: string; name: string } | undefined;
  onClearElement?: () => void;
}

export function ThreatFilterBar({
  methodCategories = [],
  frameworks = [],
  elementFilter,
  onClearElement,
}: ThreatFilterBarProps) {
  const [searchParams, setSearchParams] = useSearchParams();

  function toggle(param: string, value: string) {
    const current = searchParams.getAll(param);
    const next = new URLSearchParams(searchParams);
    if (current.includes(value)) {
      next.delete(param);
      current
        .filter((v) => v !== value)
        .forEach((v) => next.append(param, v));
    } else {
      next.append(param, value);
    }
    setSearchParams(next);
  }

  function isActive(param: string, value: string) {
    return searchParams.getAll(param).includes(value);
  }

  function clearAll() {
    setSearchParams(new URLSearchParams());
    onClearElement?.();
  }

  const hasFilters =
    !!elementFilter ||
    searchParams.getAll("findingType").length > 0 ||
    searchParams.getAll("status").length > 0 ||
    searchParams.getAll("confidence").length > 0 ||
    searchParams.getAll("method").length > 0 ||
    searchParams.getAll("framework").length > 0;

  return (
    <div className="space-y-2 rounded-lg border p-3">
      <div className="flex items-center justify-between">
        <span className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">
          Filters
        </span>
        {hasFilters && (
          <button
            onClick={clearAll}
            className="text-xs text-primary hover:underline"
          >
            Clear all
          </button>
        )}
      </div>

      {/* GAP-TH3: element filter chip — set by canvas click */}
      {elementFilter && (
        <div>
          <p className="mb-1 text-xs text-muted-foreground">Element</p>
          <div className="flex flex-wrap gap-1">
            <button
              onClick={onClearElement}
              className="flex items-center gap-1 rounded-full border border-primary bg-primary px-2 py-0.5 text-xs text-primary-foreground transition-colors"
            >
              {elementFilter.name}
              <span className="ml-0.5 opacity-70">×</span>
            </button>
          </div>
        </div>
      )}

      <FilterGroup
        label="Finding type"
        values={FINDING_TYPES}
        param="findingType"
        isActive={isActive}
        toggle={toggle}
      />
      <FilterGroup
        label="Status"
        values={STATUSES}
        param="status"
        isActive={isActive}
        toggle={toggle}
      />
      <FilterGroup
        label="Confidence"
        values={CONFIDENCES}
        param="confidence"
        isActive={isActive}
        toggle={toggle}
      />

      {methodCategories.length > 0 && (
        <FilterGroup
          label="Method"
          values={methodCategories.map((c) => ({ label: c, value: c }))}
          param="method"
          isActive={isActive}
          toggle={toggle}
        />
      )}

      {frameworks.length > 0 && (
        <FilterGroup
          label="Framework"
          values={frameworks.map((f) => ({ label: f, value: f }))}
          param="framework"
          isActive={isActive}
          toggle={toggle}
        />
      )}
    </div>
  );
}

function FilterGroup({
  label,
  values,
  param,
  isActive,
  toggle,
}: {
  label: string;
  values: Array<{ label: string; value: string }>;
  param: string;
  isActive: (p: string, v: string) => boolean;
  toggle: (p: string, v: string) => void;
}) {
  return (
    <div>
      <p className="mb-1 text-xs text-muted-foreground">{label}</p>
      <div className="flex flex-wrap gap-1">
        {values.map((v) => (
          <button
            key={v.value}
            onClick={() => toggle(param, v.value)}
            className={cn(
              "rounded-full border px-2 py-0.5 text-xs transition-colors",
              isActive(param, v.value)
                ? "border-primary bg-primary text-primary-foreground"
                : "border-muted-foreground/30 hover:border-primary/50",
            )}
          >
            {v.label}
          </button>
        ))}
      </div>
    </div>
  );
}
