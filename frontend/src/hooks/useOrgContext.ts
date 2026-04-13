import { createContext, useContext } from "react";
import type { OrgSummary } from "@/api/auth";

export interface OrgContextValue {
  currentOrg: OrgSummary | null;
  allOrgs: OrgSummary[];
  currentRole: "owner" | "member" | null;
  isOwner: boolean;
  currentUserId: string | null;
  isPlatformAdmin: boolean;
}

export const OrgContext = createContext<OrgContextValue>({
  currentOrg: null,
  allOrgs: [],
  currentRole: null,
  isOwner: false,
  currentUserId: null,
  isPlatformAdmin: false,
});

export function useOrgContext() {
  return useContext(OrgContext);
}
