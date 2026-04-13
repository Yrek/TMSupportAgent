import { renderHook } from "@testing-library/react";
import { describe, it, expect } from "vitest";
import { OrgContext, useOrgContext } from "@/hooks/useOrgContext";

describe("useOrgContext", () => {
  it("returns isOwner=true for owner role", () => {
    const value = {
      currentOrg: { id: "org1", name: "Acme", slug: "acme", role: "owner" as const },
      allOrgs: [],
      currentRole: "owner" as const,
      isOwner: true,
      currentUserId: "user1",
      isPlatformAdmin: false,
    };
    const { result } = renderHook(() => useOrgContext(), {
      wrapper: ({ children }) => (
        <OrgContext.Provider value={value}>{children}</OrgContext.Provider>
      ),
    });
    expect(result.current.isOwner).toBe(true);
  });

  it("returns isOwner=false for member role", () => {
    const value = {
      currentOrg: { id: "org1", name: "Acme", slug: "acme", role: "member" as const },
      allOrgs: [],
      currentRole: "member" as const,
      isOwner: false,
      currentUserId: "user1",
      isPlatformAdmin: false,
    };
    const { result } = renderHook(() => useOrgContext(), {
      wrapper: ({ children }) => (
        <OrgContext.Provider value={value}>{children}</OrgContext.Provider>
      ),
    });
    expect(result.current.isOwner).toBe(false);
  });
});
