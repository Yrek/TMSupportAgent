import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "./client";
import { ACTIVE_JOB_STATUSES, POLL_INTERVAL_MS, type JobStatus } from "@/lib/constants";

export interface JobSummary {
  id: string;
  title: string | null;
  status: JobStatus;
  artifactType: string | null;
  isManual?: boolean;
  createdAt: string;
  completedAt: string | null;
}

export interface JobDetail extends JobSummary {
  errorCode: string | null;
  threatCount: number | null;
  confirmedThreatCount: number | null;
}

export interface JobListFilters {
  status?: JobStatus | undefined;
  pageSize?: number | undefined;
  cursor?: string | undefined;
}

export function useJobs(orgId: string, filters?: JobListFilters) {
  return useQuery<{ data: JobSummary[]; pagination?: { nextCursor: string | null; hasMore: boolean } }>({
    queryKey: ["jobs", orgId, filters],
    queryFn: async () => {
      const params = new URLSearchParams();
      if (filters?.status) params.set("status", filters.status);
      if (filters?.pageSize) params.set("pageSize", String(filters.pageSize));
      if (filters?.cursor) params.set("cursor", filters.cursor);
      const res = await apiClient.get(`/orgs/${orgId}/jobs?${params.toString()}`);
      return res.data as { data: JobSummary[]; pagination?: { nextCursor: string | null; hasMore: boolean } };
    },
    enabled: !!orgId,
    refetchInterval: (query) => {
      const jobs = query.state.data?.data ?? [];
      const hasActive = jobs.some((j) => ACTIVE_JOB_STATUSES.includes(j.status));
      return hasActive ? POLL_INTERVAL_MS : false;
    },
  });
}

export function useJob(orgId: string, jobId: string) {
  return useQuery<JobDetail>({
    queryKey: ["job", orgId, jobId],
    queryFn: async () => {
      const res = await apiClient.get<JobDetail>(`/orgs/${orgId}/jobs/${jobId}`);
      return res.data;
    },
    enabled: !!orgId && !!jobId,
    refetchInterval: (query) => {
      const status = query.state.data?.status;
      return status && ACTIVE_JOB_STATUSES.includes(status) ? POLL_INTERVAL_MS : false;
    },
  });
}

export function useSubmitJob(orgId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (formData: FormData) => {
      const res = await apiClient.post<JobDetail>(`/orgs/${orgId}/jobs`, formData);
      return res.data;
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["jobs", orgId] });
    },
  });
}

export function useCreateManualJob(orgId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (req: { title?: string | undefined; systemPurpose?: string | undefined }) => {
      const res = await apiClient.post<JobDetail>(`/orgs/${orgId}/jobs/manual`, req);
      return res.data;
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["jobs", orgId] });
    },
  });
}

export function useDeleteJob(orgId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (jobId: string) => {
      await apiClient.delete(`/orgs/${orgId}/jobs/${jobId}`);
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["jobs", orgId] });
    },
  });
}
