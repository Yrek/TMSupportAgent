import { Link, NavLink } from "react-router-dom";
import { LayoutDashboard, Building2, ShieldCheck, LogOut, User } from "lucide-react";
import { useSignOut } from "@/api/auth";
import { cn } from "@/lib/utils";

interface AdminShellProps {
  children: React.ReactNode;
}

const navItems = [
  { to: "/admin", label: "Dashboard", icon: <LayoutDashboard className="h-4 w-4" />, end: true },
  { to: "/admin/orgs", label: "Organizations", icon: <Building2 className="h-4 w-4" /> },
];

export function AdminShell({ children }: AdminShellProps) {
  const signOut = useSignOut();

  return (
    <div className="flex min-h-screen">
      <aside className="hidden w-56 shrink-0 border-r bg-background lg:flex lg:flex-col">
        <nav className="flex h-full flex-col gap-1 p-3">
          <div className="mb-4 flex items-center gap-2 px-2 py-1">
            <ShieldCheck className="h-5 w-5 text-destructive" />
            <span className="text-sm font-semibold">Platform Admin</span>
          </div>

          <div className="flex-1 space-y-1">
            {navItems.map((item) => (
              <NavLink
                key={item.to}
                to={item.to}
                end={item.end ?? false}
                className={({ isActive }) =>
                  cn(
                    "flex items-center gap-3 rounded-md px-3 py-2 text-sm transition-colors",
                    isActive
                      ? "bg-primary/10 text-primary font-medium"
                      : "text-muted-foreground hover:bg-muted hover:text-foreground",
                  )
                }
              >
                {item.icon}
                {item.label}
              </NavLink>
            ))}
          </div>

          <div className="border-t pt-3 space-y-1">
            <Link
              to="/orgs"
              className="flex items-center gap-3 rounded-md px-3 py-2 text-sm text-muted-foreground transition-colors hover:bg-muted hover:text-foreground"
            >
              <User className="h-4 w-4" />
              Back to app
            </Link>
            <button
              onClick={() => signOut.mutate()}
              className="flex w-full items-center gap-3 rounded-md px-3 py-2 text-sm text-muted-foreground transition-colors hover:bg-muted hover:text-foreground"
            >
              <LogOut className="h-4 w-4" />
              Sign out
            </button>
          </div>
        </nav>
      </aside>

      <div className="flex flex-1 flex-col overflow-hidden">
        <div className="flex h-10 items-center gap-2 border-b bg-destructive/5 px-6">
          <ShieldCheck className="h-4 w-4 text-destructive" />
          <span className="text-xs font-medium text-destructive">Platform admin console</span>
        </div>
        <main className="flex-1 overflow-auto">{children}</main>
      </div>
    </div>
  );
}
