import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "./client";
import type { OrgSummary } from "./auth";

export interface OrgDetail {
  id: string;
  name: string;
  slug: string;
  hasCustomIdp?: boolean;
  createdAt: string;
}

export interface CreateOrgRequest {
  name: string;
  slug: string;
}

export interface UpdateOrgRequest {
  name: string;
}

export function useOrgs() {
  return useQuery<OrgSummary[]>({
    queryKey: ["orgs"],
    queryFn: async () => {
      const res = await apiClient.get<{ data: OrgSummary[] }>("/orgs");
      return res.data.data;
    },
  });
}

export function useOrg(orgId: string) {
  return useQuery<OrgDetail>({
    queryKey: ["org", orgId],
    queryFn: async () => {
      const res = await apiClient.get<OrgDetail>(`/orgs/${orgId}`);
      return res.data;
    },
    enabled: !!orgId,
  });
}

export function useCreateOrg() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (req: CreateOrgRequest) => {
      const res = await apiClient.post<OrgDetail>("/orgs", req);
      return res.data;
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["orgs"] });
      void queryClient.invalidateQueries({ queryKey: ["session"] });
    },
  });
}

export function useUpdateOrg(orgId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (req: UpdateOrgRequest) => {
      const res = await apiClient.patch<OrgDetail>(`/orgs/${orgId}`, req);
      return res.data;
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["org", orgId] });
      void queryClient.invalidateQueries({ queryKey: ["orgs"] });
      void queryClient.invalidateQueries({ queryKey: ["session"] });
    },
  });
}

export interface OrgStats {
  totalJobs: number;
  byStatus: Record<string, number>;
  activeMembers: number;
}

export interface AuditLogEntry {
  id: string;
  eventType: string;
  resourceType: string | null;
  resourceId: string | null;
  userId: string | null;
  ipAddress: string | null;
  createdAt: string;
}

export function useOrgStats(orgId: string) {
  return useQuery<OrgStats>({
    queryKey: ["org", orgId, "stats"],
    queryFn: async () => {
      const res = await apiClient.get<OrgStats>(`/orgs/${orgId}/stats`);
      return res.data;
    },
    enabled: !!orgId,
  });
}

export function useOrgAuditLog(orgId: string, page = 1) {
  return useQuery<{ data: AuditLogEntry[]; pagination: { total: number; totalPages: number } }>({
    queryKey: ["org", orgId, "audit", page],
    queryFn: async () => {
      const res = await apiClient.get(`/orgs/${orgId}/audit`, { params: { page, pageSize: 50 } });
      return res.data;
    },
    enabled: !!orgId,
  });
}

export function useDeleteOrg(orgId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () => {
      await apiClient.delete(`/orgs/${orgId}`);
    },
    onSuccess: () => {
      queryClient.clear();
      window.location.href = "/orgs";
    },
  });
}
