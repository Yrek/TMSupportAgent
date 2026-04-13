import { Link } from "react-router-dom";
import { Building2, Users, FileSearch, TrendingUp } from "lucide-react";
import { AdminShell } from "@/components/layout/AdminShell";
import { useAdminStats } from "@/api/admin";
import { Skeleton } from "@/components/ui/skeleton";
import { usePageTitle } from "@/hooks/usePageTitle";

export function AdminDashboardPage() {
  usePageTitle("Admin — Dashboard");
  const { data: stats, isLoading } = useAdminStats();

  const statCards = stats
    ? [
        { label: "Total organizations", value: stats.totalOrgs, icon: <Building2 className="h-5 w-5 text-muted-foreground" />, href: "/admin/orgs" },
        { label: "Active organizations", value: stats.activeOrgs, icon: <TrendingUp className="h-5 w-5 text-green-500" />, href: "/admin/orgs" },
        { label: "Suspended organizations", value: stats.suspendedOrgs, icon: <Building2 className="h-5 w-5 text-destructive" />, href: "/admin/orgs?suspended=true" },
        { label: "Total users", value: stats.totalUsers, icon: <Users className="h-5 w-5 text-muted-foreground" />, href: null },
        { label: "Total jobs", value: stats.totalJobs, icon: <FileSearch className="h-5 w-5 text-muted-foreground" />, href: null },
        { label: "Jobs (last 30 days)", value: stats.jobsLast30Days, icon: <TrendingUp className="h-5 w-5 text-primary" />, href: null },
      ]
    : [];

  return (
    <AdminShell>
      <div className="mx-auto max-w-5xl space-y-6 p-6">
        <h1 className="text-2xl font-bold">Dashboard</h1>

        {isLoading ? (
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {[1, 2, 3, 4, 5, 6].map((i) => (
              <Skeleton key={i} className="h-28 w-full" />
            ))}
          </div>
        ) : (
          <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
            {statCards.map((card) => {
              const inner = (
                <div className="flex items-start justify-between rounded-lg border p-4 hover:bg-muted/50 transition-colors">
                  <div>
                    <p className="text-sm text-muted-foreground">{card.label}</p>
                    <p className="mt-1 text-3xl font-bold">{card.value.toLocaleString()}</p>
                  </div>
                  {card.icon}
                </div>
              );
              return card.href ? (
                <Link key={card.label} to={card.href}>{inner}</Link>
              ) : (
                <div key={card.label}>{inner}</div>
              );
            })}
          </div>
        )}

        <div className="rounded-lg border p-4">
          <h2 className="mb-2 font-semibold">Quick actions</h2>
          <div className="flex flex-wrap gap-3">
            <Link
              to="/admin/orgs"
              className="rounded-md border px-4 py-2 text-sm transition-colors hover:bg-muted"
            >
              View all organizations
            </Link>
            <Link
              to="/admin/orgs?suspended=true"
              className="rounded-md border px-4 py-2 text-sm text-destructive transition-colors hover:bg-destructive/10"
            >
              View suspended orgs
            </Link>
          </div>
        </div>
      </div>
    </AdminShell>
  );
}
