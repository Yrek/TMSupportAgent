import { useState } from "react";
import { Link } from "react-router-dom";
import { Search, ChevronLeft, ChevronRight } from "lucide-react";
import { AdminShell } from "@/components/layout/AdminShell";
import { useAdminCreateOrg, useAdminOrgs } from "@/api/admin";
import { Skeleton } from "@/components/ui/skeleton";
import { Input } from "@/components/ui/input";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { usePageTitle } from "@/hooks/usePageTitle";
import { toast } from "sonner";

export function AdminOrgsPage() {
  usePageTitle("Admin — Organizations");
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);
  const [debouncedSearch, setDebouncedSearch] = useState("");
  const [newName, setNewName] = useState("");
  const [newSlug, setNewSlug] = useState("");
  const createOrg = useAdminCreateOrg();

  // Simple debounce
  function handleSearch(val: string) {
    setSearch(val);
    setPage(1);
    clearTimeout((window as typeof window & { _st?: ReturnType<typeof setTimeout> })._st);
    (window as typeof window & { _st?: ReturnType<typeof setTimeout> })._st = setTimeout(
      () => setDebouncedSearch(val),
      300,
    );
  }

  const { data, isLoading } = useAdminOrgs({ search: debouncedSearch || undefined, page, pageSize: 20 });

  const orgs = data?.data ?? [];
  const pagination = data?.pagination;

  async function handleCreateOrg() {
    if (!newName.trim() || !newSlug.trim()) return;
    try {
      await createOrg.mutateAsync({ name: newName.trim(), slug: newSlug.trim() });
      setNewName("");
      setNewSlug("");
      toast.success("Organization created");
    } catch {
      toast.error("Failed to create organization");
    }
  }

  return (
    <AdminShell>
      <div className="mx-auto max-w-5xl space-y-6 p-6">
        <div className="flex items-center justify-between">
          <h1 className="text-2xl font-bold">Organizations</h1>
          {pagination && (
            <span className="text-sm text-muted-foreground">
              {pagination.total.toLocaleString()} total
            </span>
          )}
        </div>

        <div className="relative">
          <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted-foreground" />
          <Input
            className="pl-9"
            placeholder="Search by name or slug…"
            value={search}
            onChange={(e) => handleSearch(e.target.value)}
          />
        </div>

        <div className="grid gap-3 rounded-lg border p-4 md:grid-cols-[1fr_1fr_auto]">
          <Input
            placeholder="Organization name"
            value={newName}
            onChange={(e) => setNewName(e.target.value)}
          />
          <Input
            placeholder="slug-name"
            value={newSlug}
            onChange={(e) => setNewSlug(e.target.value)}
          />
          <Button onClick={handleCreateOrg} disabled={createOrg.isPending}>
            {createOrg.isPending ? "Creating..." : "Create org"}
          </Button>
        </div>

        {isLoading ? (
          <div className="space-y-2">
            {[1, 2, 3, 4, 5].map((i) => <Skeleton key={i} className="h-14 w-full" />)}
          </div>
        ) : !orgs.length ? (
          <div className="rounded-lg border border-dashed p-10 text-center text-muted-foreground">
            No organizations found.
          </div>
        ) : (
          <div className="rounded-lg border divide-y">
            {orgs.map((org) => (
              <Link
                key={org.id}
                to={`/admin/orgs/${org.id}`}
                className="flex items-center justify-between px-4 py-3 hover:bg-muted/50 transition-colors"
              >
                <div className="min-w-0">
                  <div className="flex items-center gap-2">
                    <span className="font-medium truncate">{org.name}</span>
                    {org.isSuspended && (
                      <Badge variant="destructive" className="shrink-0">Suspended</Badge>
                    )}
                  </div>
                  <p className="text-xs text-muted-foreground">
                    /{org.slug} · {org.memberCount} member{org.memberCount !== 1 ? "s" : ""} · {org.jobCount} job{org.jobCount !== 1 ? "s" : ""}
                  </p>
                </div>
                <span className="ml-4 shrink-0 text-xs text-muted-foreground">
                  {new Date(org.createdAt).toLocaleDateString()}
                </span>
              </Link>
            ))}
          </div>
        )}

        {pagination && pagination.totalPages > 1 && (
          <div className="flex items-center justify-center gap-3">
            <button
              onClick={() => setPage((p) => Math.max(1, p - 1))}
              disabled={page === 1}
              className="rounded-md border p-1.5 disabled:opacity-40"
            >
              <ChevronLeft className="h-4 w-4" />
            </button>
            <span className="text-sm text-muted-foreground">
              Page {page} of {pagination.totalPages}
            </span>
            <button
              onClick={() => setPage((p) => Math.min(pagination.totalPages, p + 1))}
              disabled={page === pagination.totalPages}
              className="rounded-md border p-1.5 disabled:opacity-40"
            >
              <ChevronRight className="h-4 w-4" />
            </button>
          </div>
        )}
      </div>
    </AdminShell>
  );
}
