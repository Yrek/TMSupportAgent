import type { ElementType } from "@/lib/constants";

export interface ElementTypeConfig {
  label: string;
  icon: string;
  borderClass: string;
  textClass: string;
  bgClass: string;
  shape: "rectangle" | "circle" | "cylinder" | "double-border" | "badge" | "rounded" | "dashed";
}

export const ELEMENT_TYPE_CONFIG: Record<ElementType, ElementTypeConfig> = {
  Component: {
    label: "Component",
    icon: "🔷",
    borderClass: "border-slate-400",
    textClass: "text-slate-600",
    bgClass: "bg-slate-50",
    shape: "rectangle",
  },
  Actor: {
    label: "Actor",
    icon: "👤",
    borderClass: "border-indigo-400",
    textClass: "text-indigo-600",
    bgClass: "bg-indigo-50",
    shape: "circle",
  },
  DataFlow: {
    label: "Data Flow",
    icon: "➡️",
    borderClass: "border-gray-400",
    textClass: "text-gray-600",
    bgClass: "bg-gray-50",
    shape: "rectangle",
  },
  TrustBoundary: {
    label: "Trust Boundary",
    icon: "🔒",
    borderClass: "border-teal-400 border-dashed",
    textClass: "text-teal-600",
    bgClass: "bg-teal-50",
    shape: "dashed",
  },
  DataStore: {
    label: "Data Store",
    icon: "🗄️",
    borderClass: "border-amber-400",
    textClass: "text-amber-600",
    bgClass: "bg-amber-50",
    shape: "cylinder",
  },
  ExternalSystem: {
    label: "External System",
    icon: "🌐",
    borderClass: "border-violet-400",
    textClass: "text-violet-600",
    bgClass: "bg-violet-50",
    shape: "double-border",
  },
  Identity: {
    label: "Identity",
    icon: "🪪",
    borderClass: "border-pink-400",
    textClass: "text-pink-600",
    bgClass: "bg-pink-50",
    shape: "badge",
  },
  BackgroundJob: {
    label: "Background Job",
    icon: "⚙️",
    borderClass: "border-orange-400",
    textClass: "text-orange-600",
    bgClass: "bg-orange-50",
    shape: "rounded",
  },
  LlmBoundary: {
    label: "LLM Boundary",
    icon: "🤖",
    borderClass: "border-purple-400",
    textClass: "text-purple-600",
    bgClass: "bg-purple-50",
    shape: "rectangle",
  },
};
