import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "./client";

export interface AdminOrgDto {
  id: string;
  name: string;
  slug: string;
  isSuspended: boolean;
  suspendedAt: string | null;
  createdAt: string;
  memberCount: number;
  jobCount: number;
}

export interface AdminSystemStats {
  totalOrgs: number;
  activeOrgs: number;
  suspendedOrgs: number;
  totalUsers: number;
  totalJobs: number;
  jobsLast30Days: number;
}

export interface CreateAdminOrgRequest {
  name: string;
  slug: string;
}

interface ListOrgsParams {
  search?: string | undefined;
  page?: number | undefined;
  pageSize?: number | undefined;
}

interface PaginatedOrgs {
  data: AdminOrgDto[];
  pagination: { page: number; pageSize: number; total: number; totalPages: number };
}

export function useAdminStats() {
  return useQuery<AdminSystemStats>({
    queryKey: ["admin", "stats"],
    queryFn: async () => {
      const res = await apiClient.get<AdminSystemStats>("/admin/stats");
      return res.data;
    },
  });
}

export function useAdminOrgs(params: ListOrgsParams = {}) {
  return useQuery<PaginatedOrgs>({
    queryKey: ["admin", "orgs", params],
    queryFn: async () => {
      const res = await apiClient.get<PaginatedOrgs>("/admin/orgs", { params });
      return res.data;
    },
  });
}

export function useAdminOrg(orgId: string) {
  return useQuery<AdminOrgDto>({
    queryKey: ["admin", "orgs", orgId],
    queryFn: async () => {
      const res = await apiClient.get<AdminOrgDto>(`/admin/orgs/${orgId}`);
      return res.data;
    },
  });
}

export function useSuspendOrg(orgId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async () => {
      const res = await apiClient.post<AdminOrgDto>(`/admin/orgs/${orgId}/suspend`);
      return res.data;
    },
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ["admin", "orgs"] });
    },
  });
}

export function useUnsuspendOrg(orgId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async () => {
      const res = await apiClient.post<AdminOrgDto>(`/admin/orgs/${orgId}/unsuspend`);
      return res.data;
    },
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ["admin", "orgs"] });
    },
  });
}

export function useAdminDeleteOrg(orgId: string) {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async () => {
      await apiClient.delete(`/admin/orgs/${orgId}`);
    },
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ["admin", "orgs"] });
      void qc.invalidateQueries({ queryKey: ["admin", "stats"] });
    },
  });
}

export function useAdminCreateOrg() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: async (req: CreateAdminOrgRequest) => {
      const res = await apiClient.post<AdminOrgDto>("/admin/orgs", req);
      return res.data;
    },
    onSuccess: () => {
      void qc.invalidateQueries({ queryKey: ["admin", "orgs"] });
      void qc.invalidateQueries({ queryKey: ["admin", "stats"] });
    },
  });
}
