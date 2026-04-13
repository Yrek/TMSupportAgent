import { useNavigate } from "react-router-dom";
import { ChevronsUpDown, Building2, PlusCircle } from "lucide-react";
import { useOrgContext } from "@/hooks/useOrgContext";
import { Badge } from "@/components/ui/badge";
import { useState, useRef, useEffect } from "react";
import { cn } from "@/lib/utils";

export function OrgSwitcher() {
  const { currentOrg, allOrgs } = useOrgContext();
  const navigate = useNavigate();
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    function handleClickOutside(e: MouseEvent) {
      if (ref.current && !ref.current.contains(e.target as Node)) {
        setOpen(false);
      }
    }
    document.addEventListener("mousedown", handleClickOutside);
    return () => document.removeEventListener("mousedown", handleClickOutside);
  }, []);

  if (!currentOrg) return null;

  return (
    <div ref={ref} className="relative">
      <button
        onClick={() => setOpen(!open)}
        className="flex w-full items-center gap-2 rounded-md px-2 py-1.5 text-sm hover:bg-muted transition-colors"
        aria-haspopup="listbox"
        aria-expanded={open}
      >
        <Building2 className="h-4 w-4 shrink-0 text-muted-foreground" />
        <span className="flex-1 truncate text-left font-medium">{currentOrg.name}</span>
        <Badge variant={currentOrg.role === "owner" ? "default" : "secondary"} className="text-xs">
          {currentOrg.role === "owner" ? "Owner" : "Member"}
        </Badge>
        <ChevronsUpDown className="h-3 w-3 shrink-0 text-muted-foreground" />
      </button>

      {open && (
        <div
          role="listbox"
          className="absolute left-0 top-full z-50 mt-1 w-full rounded-md border bg-popover p-1 shadow-md"
        >
          {allOrgs.map((org) => (
            <button
              key={org.id}
              role="option"
              aria-selected={org.id === currentOrg.id}
              onClick={() => {
                setOpen(false);
                navigate(`/orgs/${org.id}/jobs`);
              }}
              className={cn(
                "flex w-full items-center gap-2 rounded px-2 py-1.5 text-sm transition-colors",
                org.id === currentOrg.id
                  ? "bg-primary/10 text-primary"
                  : "hover:bg-muted",
              )}
            >
              <Building2 className="h-3.5 w-3.5 text-muted-foreground" />
              <span className="flex-1 truncate">{org.name}</span>
              <Badge variant={org.role === "owner" ? "default" : "secondary"} className="text-xs">
                {org.role === "owner" ? "Owner" : "Member"}
              </Badge>
            </button>
          ))}

          <div className="my-1 border-t" />

          <button
            onClick={() => {
              setOpen(false);
              navigate("/orgs/new");
            }}
            className="flex w-full items-center gap-2 rounded px-2 py-1.5 text-sm text-muted-foreground hover:bg-muted transition-colors"
          >
            <PlusCircle className="h-3.5 w-3.5" />
            Create new organisation
          </button>
        </div>
      )}
    </div>
  );
}
