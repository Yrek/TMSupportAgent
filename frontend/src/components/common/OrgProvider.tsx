import { useParams } from "react-router-dom";
import { useEffect, useRef } from "react";
import { useSession } from "@/api/auth";
import { OrgContext } from "@/hooks/useOrgContext";
import { useAuth } from "@workos-inc/authkit-react";
import { setAccessToken, registerSilentRefresh } from "@/api/client";
import { isDevAuth } from "@/lib/env";

interface OrgProviderProps {
  children: React.ReactNode;
}

// WorkOS variant — requests org-scoped tokens via switchToOrganization.
// Only rendered when AuthKitProvider is in the tree (isDevAuth=false).
function OrgProviderWorkOS({ children }: OrgProviderProps) {
  const { orgId } = useParams<{ orgId: string }>();
  const { data: session } = useSession();
  const { user, getAccessToken, switchToOrganization } = useAuth();
  const lastSwitchedOrgRef = useRef<string | null>(null);
  const refreshRegisteredRef = useRef(false);

  const allOrgs = session?.orgs ?? [];
  const currentOrg = orgId ? (allOrgs.find((o) => o.id === orgId) ?? null) : null;
  const currentRole = currentOrg?.role ?? null;
  const isOwner = currentRole === "owner";
  const currentUserId = session?.userId ?? null;
  const isPlatformAdmin = session?.isPlatformAdmin ?? false;
  const workosOrgId = currentOrg?.workosOrgId ?? null;

  useEffect(() => {
    if (refreshRegisteredRef.current) return;

    registerSilentRefresh(async () => {
      if (lastSwitchedOrgRef.current) {
        await switchToOrganization({ organizationId: lastSwitchedOrgRef.current });
      }
      const t = await getAccessToken();
      return t ?? null;
    });

    refreshRegisteredRef.current = true;
  }, [getAccessToken, switchToOrganization]);

  useEffect(() => {
    if (!user || !workosOrgId) return;
    if (lastSwitchedOrgRef.current === workosOrgId) return;

    lastSwitchedOrgRef.current = workosOrgId;
    void switchToOrganization({ organizationId: workosOrgId })
      .then(() => getAccessToken())
      .then((token) => {
        setAccessToken(token ?? null);
      })
      .catch(() => {
        setAccessToken(null);
      });
  }, [workosOrgId, user, getAccessToken, switchToOrganization]);

  return (
    <OrgContext.Provider value={{ currentOrg, allOrgs, currentRole, isOwner, currentUserId, isPlatformAdmin }}>
      {children}
    </OrgContext.Provider>
  );
}

// Dev auth variant — token already carries internal org_id; no org switching needed.
function OrgProviderDev({ children }: OrgProviderProps) {
  const { orgId } = useParams<{ orgId: string }>();
  const { data: session } = useSession();

  const allOrgs = session?.orgs ?? [];
  const currentOrg = orgId ? (allOrgs.find((o) => o.id === orgId) ?? null) : null;
  const currentRole = currentOrg?.role ?? null;
  const isOwner = currentRole === "owner";
  const currentUserId = session?.userId ?? null;
  const isPlatformAdmin = session?.isPlatformAdmin ?? false;

  return (
    <OrgContext.Provider value={{ currentOrg, allOrgs, currentRole, isOwner, currentUserId, isPlatformAdmin }}>
      {children}
    </OrgContext.Provider>
  );
}

export function OrgProvider({ children }: OrgProviderProps) {
  return isDevAuth
    ? <OrgProviderDev>{children}</OrgProviderDev>
    : <OrgProviderWorkOS>{children}</OrgProviderWorkOS>;
}
