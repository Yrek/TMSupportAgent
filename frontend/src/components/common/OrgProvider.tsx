import { useParams } from "react-router-dom";
import { useSession } from "@/api/auth";
import { OrgContext } from "@/hooks/useOrgContext";
import { useAuth } from "@workos-inc/authkit-react";

interface OrgProviderProps {
  children: React.ReactNode;
}

export function OrgProvider({ children }: OrgProviderProps) {
  const { orgId } = useParams<{ orgId: string }>();
  const { data: session } = useSession();
  const { user } = useAuth();

  const allOrgs = session?.orgs ?? [];
  const currentOrg = orgId ? (allOrgs.find((o) => o.id === orgId) ?? null) : null;
  const currentRole = currentOrg?.role ?? null;
  const isOwner = currentRole === "owner";

  // userId comes from session; display info from WorkOS AuthKit
  const currentUserId = session?.userId ?? null;
  const isPlatformAdmin = session?.isPlatformAdmin ?? false;
  void user; // WorkOS user object available for displayName/email in AppShell

  return (
    <OrgContext.Provider value={{ currentOrg, allOrgs, currentRole, isOwner, currentUserId, isPlatformAdmin }}>
      {children}
    </OrgContext.Provider>
  );
}
