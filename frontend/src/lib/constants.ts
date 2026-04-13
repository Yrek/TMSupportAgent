export const JOB_STATUSES = [
  "Pending",
  "Parsing",
  "Normalizing",
  "AwaitingReview",
  "Classifying",
  "Analyzing",
  "Synthesizing",
  "Complete",
  "Failed",
  "Partial",
] as const;

export type JobStatus = (typeof JOB_STATUSES)[number];

export const ACTIVE_JOB_STATUSES: JobStatus[] = [
  "Pending",
  "Parsing",
  "Normalizing",
  "Classifying",
  "Analyzing",
  "Synthesizing",
];

export const TERMINAL_JOB_STATUSES: JobStatus[] = ["Complete", "Failed", "Partial"];

export const ELEMENT_TYPES = [
  "Component",
  "Actor",
  "DataFlow",
  "TrustBoundary",
  "DataStore",
  "ExternalSystem",
  "Identity",
  "BackgroundJob",
  "LlmBoundary",
] as const;

export type ElementType = (typeof ELEMENT_TYPES)[number];

export const CORRECTION_TYPES = [
  "Update",
  "MarkIncorrect",
  "MarkAssumed",
  "MarkConfirmed",
  "AddNote",
] as const;

export type CorrectionType = (typeof CORRECTION_TYPES)[number];

export const THREAT_STATUSES = ["Open", "Accepted", "Mitigated", "Rejected"] as const;
export type ThreatStatus = (typeof THREAT_STATUSES)[number];

export const MAX_FILE_SIZE_BYTES = 10 * 1024 * 1024; // 10 MB
export const MAX_FILE_SIZE_LABEL = "10 MB";

export const ALLOWED_EXTENSIONS = [
  ".png",
  ".jpg",
  ".jpeg",
  ".gif",
  ".webp",
  ".puml",
  ".txt",
  ".md",
  ".mmd",
  ".drawio",
  ".xml",
] as const;

export const POLL_INTERVAL_MS = 10_000;
