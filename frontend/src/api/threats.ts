import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "./client";
import type { ThreatStatus } from "@/lib/constants";

export interface Mitigation {
  id: string;
  title: string;
  description: string;
  priority: "critical" | "high" | "medium" | "low";
  category: string | null;
}

export interface FrameworkMapping {
  framework: string;
  reference: string;
  mappingType: "direct" | "approximate";
}

export interface ThreatNote {
  id: string;
  body: string;
  createdBy: string;
  createdAt: string;
}

export interface Threat {
  id: string;
  identifier: string;
  title: string;
  methodCategory: string;
  affectedElementIds: string[];
  description: string;
  attackScenario: string;
  preconditions: string | null;
  impactedAssets: string[];
  securityImpact: string | null;
  privacyImpact: string | null;
  existingControls: string | null;
  controlGaps: string | null;
  confidence: "High" | "Medium" | "Low";
  evidenceBasis: string[];
  evidenceStrength: "Direct" | "Inferred" | "AssumptionDependent";
  assumptions: string | null;
  findingType: "Confirmed" | "Conditional" | "UserAdded";
  status: ThreatStatus;
  source: "System" | "User";
  mitigations: Mitigation[];
  frameworkMappings: FrameworkMapping[];
  notes: ThreatNote[];
}

export interface ThreatFilters {
  findingType?: string | undefined;
  status?: ThreatStatus | undefined;
  elementId?: string | undefined;
}

export interface AddThreatRequest {
  title: string;
  methodCategory: string;
  description: string;
  attackScenario: string;
  affectedElementIds?: string[] | undefined;
  preconditions?: string | undefined;
  impactedAssets?: string[] | undefined;
  securityImpact?: string | undefined;
  privacyImpact?: string | undefined;
}

export function useThreats(orgId: string, jobId: string, filters?: ThreatFilters) {
  return useQuery<Threat[]>({
    queryKey: ["threats", orgId, jobId, filters],
    queryFn: async () => {
      const params = new URLSearchParams();
      if (filters?.findingType) params.set("findingType", filters.findingType);
      if (filters?.status) params.set("status", filters.status);
      if (filters?.elementId) params.set("elementId", filters.elementId);
      const res = await apiClient.get<{ data: Threat[] }>(
        `/orgs/${orgId}/jobs/${jobId}/threats?${params.toString()}`,
      );
      return res.data.data;
    },
    enabled: !!orgId && !!jobId,
  });
}

export function useThreat(orgId: string, jobId: string, threatId: string) {
  return useQuery<Threat>({
    queryKey: ["threat", orgId, jobId, threatId],
    queryFn: async () => {
      const res = await apiClient.get<Threat>(
        `/orgs/${orgId}/jobs/${jobId}/threats/${threatId}`,
      );
      return res.data;
    },
    enabled: !!orgId && !!jobId && !!threatId,
  });
}

export function useAddThreat(orgId: string, jobId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (req: AddThreatRequest) => {
      const res = await apiClient.post<Threat>(`/orgs/${orgId}/jobs/${jobId}/threats`, req);
      return res.data;
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["threats", orgId, jobId] });
    },
  });
}

export function useUpdateThreatStatus(orgId: string, jobId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ threatId, status }: { threatId: string; status: ThreatStatus }) => {
      const res = await apiClient.patch<Threat>(
        `/orgs/${orgId}/jobs/${jobId}/threats/${threatId}/status`,
        { status },
      );
      return res.data;
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["threats", orgId, jobId] });
    },
  });
}

export function useAddThreatNote(orgId: string, jobId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ threatId, body }: { threatId: string; body: string }) => {
      const res = await apiClient.post<ThreatNote>(
        `/orgs/${orgId}/jobs/${jobId}/threats/${threatId}/notes`,
        { body },
      );
      return res.data;
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["threats", orgId, jobId] });
    },
  });
}

export function useAnalysis(orgId: string, jobId: string) {
  return useQuery<unknown>({
    queryKey: ["analysis", orgId, jobId],
    queryFn: async () => {
      const res = await apiClient.get(`/orgs/${orgId}/jobs/${jobId}/analysis`);
      return res.data;
    },
    enabled: !!orgId && !!jobId,
    staleTime: Infinity, // analysis blob doesn't change after job completes
  });
}

export function useExportAnalysis(orgId: string, jobId: string) {
  return useMutation({
    mutationFn: async () => {
      const res = await apiClient.get(`/orgs/${orgId}/jobs/${jobId}/export`, {
        responseType: "blob",
      });
      const url = URL.createObjectURL(res.data as Blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = `threat-model-${jobId}.json`;
      a.click();
      URL.revokeObjectURL(url);
    },
  });
}
