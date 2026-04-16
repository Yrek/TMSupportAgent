import { useParams } from "react-router-dom";
import { useEffect, useRef } from "react";
import { useSession } from "@/api/auth";
import { OrgContext } from "@/hooks/useOrgContext";
import { useAuth } from "@workos-inc/authkit-react";
import { setAccessToken, registerSilentRefresh } from "@/api/client";

interface OrgProviderProps {
  children: React.ReactNode;
}

export function OrgProvider({ children }: OrgProviderProps) {
  const { orgId } = useParams<{ orgId: string }>();
  const { data: session } = useSession();
  const { user, getAccessToken, switchToOrganization } = useAuth();
  const lastSwitchedOrgRef = useRef<string | null>(null);
  const refreshRegisteredRef = useRef(false);

  const allOrgs = session?.orgs ?? [];
  const currentOrg = orgId ? (allOrgs.find((o) => o.id === orgId) ?? null) : null;
  const currentRole = currentOrg?.role ?? null;
  const isOwner = currentRole === "owner";

  // userId comes from session; display info from WorkOS AuthKit
  const currentUserId = session?.userId ?? null;
  const isPlatformAdmin = session?.isPlatformAdmin ?? false;

  // When the current org is known, refresh the in-memory token to an org-scoped one.
  // This puts WorkOS org_id into the JWT so TenantContextMiddleware can resolve the internal org.
  const workosOrgId = currentOrg?.workosOrgId ?? null;

  // Register exactly one silent-refresh callback; it always uses the most recently selected org.
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
        // Avoid retry storms here; route guards handle re-auth if needed.
        setAccessToken(null);
      });
  }, [workosOrgId, user, getAccessToken, switchToOrganization]);

  return (
    <OrgContext.Provider value={{ currentOrg, allOrgs, currentRole, isOwner, currentUserId, isPlatformAdmin }}>
      {children}
    </OrgContext.Provider>
  );
}
