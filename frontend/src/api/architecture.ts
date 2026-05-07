import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { apiClient } from "./client";
import type { ElementType, CorrectionType } from "@/lib/constants";

export interface Correction {
  id: string;
  correctionType: string;
  fieldName: string | null;
  originalValue: string | null;
  correctedValue: string | null;
  note: string | null;
  correctedBy: string;
  createdAt: string;
}

export interface ArchitectureElement {
  id: string;
  elementType: ElementType;
  name: string;
  description: string | null;
  properties: Record<string, unknown>;
  source: "Extracted" | "UserAdded";
  extractionConfidence: "High" | "Medium" | "Low" | null;
  createdAt: string;
  corrections: Correction[];
}

export interface DeploymentContext {
  environment: "aws" | "azure" | "gcp" | "on_prem" | "hybrid" | "unknown";
  containerized: boolean;
  serverless: boolean;
  infraControls: string[];
}

export interface ArchitectureModel {
  id: string;
  jobId: string;
  version: number;
  classification: string[];
  systemPurpose: string | null;
  assumptions: Array<{ text: string; confirmed: boolean }>;
  gaps: string[];
  clarificationQuestions: Array<{ question: string; priority: string; topic?: string }>;
  deploymentContext: DeploymentContext | null;
  isConfirmed: boolean;
  confirmedAt: string | null;
  createdAt: string;
  updatedAt: string;
  elements: ArchitectureElement[];
}

export interface AddElementRequest {
  elementType: string;
  name: string;
  description?: string | undefined;
  properties?: Record<string, unknown> | undefined;
}

export interface PatchElementRequest {
  name?: string | undefined;
  description?: string | undefined;
  properties?: Record<string, unknown> | undefined;
}

export interface CorrectElementRequest {
  correctionType: CorrectionType;
  fieldName?: string | undefined;
  originalValue?: string | undefined;
  correctedValue?: string | undefined;
  note?: string | undefined;
}

export function useArchitecture(orgId: string, jobId: string) {
  return useQuery<ArchitectureModel>({
    queryKey: ["architecture", orgId, jobId],
    queryFn: async () => {
      const res = await apiClient.get<ArchitectureModel>(
        `/orgs/${orgId}/jobs/${jobId}/architecture`,
      );
      return res.data;
    },
    enabled: !!orgId && !!jobId,
  });
}

export function useAddElement(orgId: string, jobId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (req: AddElementRequest) => {
      const res = await apiClient.post<ArchitectureElement>(
        `/orgs/${orgId}/jobs/${jobId}/elements`,
        req,
      );
      return res.data;
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["architecture", orgId, jobId] });
    },
  });
}

export function usePatchElement(orgId: string, jobId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({ elementId, req }: { elementId: string; req: PatchElementRequest }) => {
      const res = await apiClient.patch<ArchitectureElement>(
        `/orgs/${orgId}/jobs/${jobId}/elements/${elementId}`,
        req,
      );
      return res.data;
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["architecture", orgId, jobId] });
    },
  });
}

export function useDeleteElement(orgId: string, jobId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (elementId: string) => {
      await apiClient.delete(`/orgs/${orgId}/jobs/${jobId}/elements/${elementId}`);
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["architecture", orgId, jobId] });
    },
  });
}

export function useCorrectElement(orgId: string, jobId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async ({
      elementId,
      req,
    }: {
      elementId: string;
      req: CorrectElementRequest;
    }) => {
      const res = await apiClient.post<ArchitectureElement>(
        `/orgs/${orgId}/jobs/${jobId}/elements/${elementId}`,
        req,
      );
      return res.data;
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["architecture", orgId, jobId] });
    },
  });
}

export interface PatchDeploymentContextRequest {
  environment: DeploymentContext["environment"];
  containerized: boolean;
  serverless: boolean;
  infraControls: string[];
}

export function useUpdateDeploymentContext(orgId: string, jobId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (req: PatchDeploymentContextRequest) => {
      const res = await apiClient.patch<ArchitectureModel>(
        `/orgs/${orgId}/jobs/${jobId}/architecture/deployment-context`,
        req,
      );
      return res.data;
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["architecture", orgId, jobId] });
    },
  });
}

export function useConfirmArchitecture(orgId: string, jobId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (req?: { note?: string | undefined; selectedMethods?: string[] | undefined }) => {
      const res = await apiClient.post<ArchitectureModel>(
        `/orgs/${orgId}/jobs/${jobId}/architecture/confirm`,
        req ?? {},
      );
      return res.data;
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["architecture", orgId, jobId] });
      void queryClient.invalidateQueries({ queryKey: ["job", orgId, jobId] });
    },
  });
}

export function useReanalyzeJob(orgId: string, jobId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async () => {
      const res = await apiClient.post(`/orgs/${orgId}/jobs/${jobId}/architecture/reanalyze`);
      return res.data;
    },
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["job", orgId, jobId] });
      void queryClient.invalidateQueries({ queryKey: ["architecture", orgId, jobId] });
      void queryClient.invalidateQueries({ queryKey: ["threats", orgId, jobId] });
    },
  });
}
