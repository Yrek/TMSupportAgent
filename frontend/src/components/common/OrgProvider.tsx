import { useParams } from "react-router-dom";
import { useEffect } from "react";
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

  const allOrgs = session?.orgs ?? [];
  const currentOrg = orgId ? (allOrgs.find((o) => o.id === orgId) ?? null) : null;
  const currentRole = currentOrg?.role ?? null;
  const isOwner = currentRole === "owner";

  // userId comes from session; display info from WorkOS AuthKit
  const currentUserId = session?.userId ?? null;
  const isPlatformAdmin = session?.isPlatformAdmin ?? false;
  void user; // WorkOS user object available for displayName/email in AppShell

  // When the current org is known, refresh the in-memory token to an org-scoped one.
  // This puts WorkOS org_id into the JWT so TenantContextMiddleware can resolve the
  // internal org — the backend never trusts the URL param for tenant scoping (CLAUDE.md §8.2).
  const workosOrgId = currentOrg?.workosOrgId ?? null;
  useEffect(() => {
    if (!workosOrgId) return;

    void switchToOrganization({ organizationId: workosOrgId })
      .then(() => getAccessToken({ forceRefresh: true }))
      .then((token) => {
        setAccessToken(token ?? null);
      });
    registerSilentRefresh(async () => {
      await switchToOrganization({ organizationId: workosOrgId });
      const t = await getAccessToken({ forceRefresh: true });
      return t ?? null;
    });
  }, [workosOrgId, getAccessToken, switchToOrganization]);

  return (
    <OrgContext.Provider value={{ currentOrg, allOrgs, currentRole, isOwner, currentUserId, isPlatformAdmin }}>
      {children}
    </OrgContext.Provider>
  );
}
