import { Link, NavLink, useParams } from "react-router-dom";
import { LayoutDashboard, Settings, User, LogOut, ShieldCheck, Menu, X, Shield } from "lucide-react";
import { useState } from "react";
import { useSignOut, useSession } from "@/api/auth";
import { useOrgContext } from "@/hooks/useOrgContext";
import { OrgSwitcher } from "./OrgSwitcher";
import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";

interface AppShellProps {
  children: React.ReactNode;
}

interface NavItem {
  to: string;
  label: string;
  icon: React.ReactNode;
}

function useNavItems(orgId: string | undefined): NavItem[] {
  if (!orgId) return [];
  return [
    { to: `/orgs/${orgId}/jobs`, label: "Jobs", icon: <LayoutDashboard className="h-4 w-4" /> },
    { to: `/orgs/${orgId}/settings`, label: "Settings", icon: <Settings className="h-4 w-4" /> },
  ];
}

export function AppShell({ children }: AppShellProps) {
  const { orgId } = useParams<{ orgId?: string }>();
  const { currentOrg } = useOrgContext();
  const signOut = useSignOut();
  const { data: session } = useSession();
  const navItems = useNavItems(orgId);
  const [mobileOpen, setMobileOpen] = useState(false);
  const isPlatformAdmin = session?.isPlatformAdmin ?? false;

  function handleSignOut() {
    signOut.mutate();
  }

  const sidebar = (
    <nav className="flex h-full flex-col gap-1 p-3">
      <div className="mb-4 flex items-center gap-2 px-2 py-1">
        <ShieldCheck className="h-5 w-5 text-primary" />
        <span className="text-sm font-semibold">Threat Modeling</span>
      </div>

      {currentOrg && <OrgSwitcher />}

      <div className="mt-2 flex-1 space-y-1">
        {navItems.map((item) => (
          <NavLink
            key={item.to}
            to={item.to}
            className={({ isActive }) =>
              cn(
                "flex items-center gap-3 rounded-md px-3 py-2 text-sm transition-colors",
                isActive
                  ? "bg-primary/10 text-primary font-medium"
                  : "text-muted-foreground hover:bg-muted hover:text-foreground",
              )
            }
            onClick={() => setMobileOpen(false)}
          >
            {item.icon}
            {item.label}
          </NavLink>
        ))}
      </div>

      <div className="border-t pt-3 space-y-1">
        {isPlatformAdmin && (
          <NavLink
            to="/admin"
            className={({ isActive }) =>
              cn(
                "flex items-center gap-3 rounded-md px-3 py-2 text-sm transition-colors",
                isActive
                  ? "bg-destructive/10 text-destructive font-medium"
                  : "text-muted-foreground hover:bg-muted hover:text-foreground",
              )
            }
            onClick={() => setMobileOpen(false)}
          >
            <Shield className="h-4 w-4" />
            Admin console
          </NavLink>
        )}
        <NavLink
          to="/me"
          className={({ isActive }) =>
            cn(
              "flex items-center gap-3 rounded-md px-3 py-2 text-sm transition-colors",
              isActive
                ? "bg-primary/10 text-primary font-medium"
                : "text-muted-foreground hover:bg-muted hover:text-foreground",
            )
          }
          onClick={() => setMobileOpen(false)}
        >
          <User className="h-4 w-4" />
          Profile
        </NavLink>

        <button
          onClick={handleSignOut}
          className="flex w-full items-center gap-3 rounded-md px-3 py-2 text-sm text-muted-foreground transition-colors hover:bg-muted hover:text-foreground"
        >
          <LogOut className="h-4 w-4" />
          Sign out
        </button>
      </div>
    </nav>
  );

  return (
    <div className="flex min-h-screen">
      {/* Desktop sidebar */}
      <aside className="hidden w-56 shrink-0 border-r bg-background lg:flex lg:flex-col">
        {sidebar}
      </aside>

      {/* Mobile drawer */}
      {mobileOpen && (
        <div className="fixed inset-0 z-50 lg:hidden">
          <button
            type="button"
            className="absolute inset-0 bg-black/50"
            aria-label="Close menu overlay"
            onClick={() => setMobileOpen(false)}
          />
          <aside className="absolute left-0 top-0 h-full w-56 bg-background border-r">
            {sidebar}
          </aside>
        </div>
      )}

      <div className="flex flex-1 flex-col overflow-hidden">
        {/* Mobile top bar */}
        <header className="flex h-12 items-center gap-3 border-b px-4 lg:hidden">
          <Button
            variant="ghost"
            size="icon"
            onClick={() => setMobileOpen(!mobileOpen)}
            aria-label={mobileOpen ? "Close menu" : "Open menu"}
          >
            {mobileOpen ? <X className="h-5 w-5" /> : <Menu className="h-5 w-5" />}
          </Button>
          <Link to="/" className="flex items-center gap-2">
            <ShieldCheck className="h-5 w-5 text-primary" />
            <span className="text-sm font-semibold">Threat Modeling</span>
          </Link>
        </header>

        {/* Narrow-screen warning */}
        <div className="hidden max-[1023px]:flex items-center justify-center p-4 bg-amber-50 text-amber-800 text-sm border-b">
          This application is best viewed on a desktop screen (1024px or wider).
        </div>

        <main className="flex-1 overflow-auto">{children}</main>
      </div>
    </div>
  );
}
