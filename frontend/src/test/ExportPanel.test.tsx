/**
 * ExportPanel unit tests
 */
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ExportPanel } from "@/components/analysis/ExportPanel";

const mockMutateAsync = vi.fn().mockResolvedValue(undefined);

vi.mock("@/api/threats", () => ({
  useExportAnalysis: () => ({
    mutateAsync: mockMutateAsync,
    isPending: false,
  }),
}));

function renderPanel(analysisData: unknown = { systemSummary: "Test system" }) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  render(
    <QueryClientProvider client={queryClient}>
      <ExportPanel orgId="org-1" jobId="job-abc" analysisData={analysisData} />
    </QueryClientProvider>,
  );
}

beforeEach(() => {
  mockMutateAsync.mockClear();
  vi.stubGlobal("URL", {
    createObjectURL: vi.fn(() => "blob:mock"),
    revokeObjectURL: vi.fn(),
  });
});

describe("ExportPanel", () => {
  it("renders all export buttons", () => {
    renderPanel();
    expect(screen.getByRole("button", { name: /download json/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /download markdown/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /download mermaid/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /download tm-bom/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /download threat dragon v2/i })).toBeInTheDocument();
  });

  it("calls useExportAnalysis mutateAsync when Download JSON is clicked", async () => {
    renderPanel();
    await userEvent.click(screen.getByRole("button", { name: /download json/i }));
    await waitFor(() => {
      expect(mockMutateAsync).toHaveBeenCalledOnce();
    });
  });

  it("does not include auth token in download UI text", async () => {
    renderPanel();
    await userEvent.click(screen.getByRole("button", { name: /download json/i }));
    await waitFor(() => {
      const btn = screen.getByRole("button", { name: /download json/i });
      expect(btn.textContent).not.toMatch(/bearer|token|key/i);
    });
  });

  it("triggers a Blob download when Download Markdown is clicked", async () => {
    const anchors: HTMLAnchorElement[] = [];
    const origCreate = document.createElement.bind(document);
    vi.spyOn(document, "createElement").mockImplementation((tag: string) => {
      const el = origCreate(tag);
      if (tag === "a") anchors.push(el as HTMLAnchorElement);
      return el;
    });

    renderPanel({ systemSummary: "My secure system" });
    await userEvent.click(screen.getByRole("button", { name: /download markdown/i }));

    await waitFor(() => {
      expect(anchors.length).toBeGreaterThan(0);
      const a = anchors[anchors.length - 1];
      if (!a) return;
      expect(a.download).toMatch(/threat-model-job-abc\.md/);
    });

    vi.restoreAllMocks();
  });

  it("triggers a Mermaid diagram download", async () => {
    const anchors: HTMLAnchorElement[] = [];
    const origCreate = document.createElement.bind(document);
    vi.spyOn(document, "createElement").mockImplementation((tag: string) => {
      const el = origCreate(tag);
      if (tag === "a") anchors.push(el as HTMLAnchorElement);
      return el;
    });

    renderPanel({ systemSummary: "System" });
    await userEvent.click(screen.getByRole("button", { name: /download mermaid/i }));

    await waitFor(() => {
      expect(anchors.length).toBeGreaterThan(0);
      const a = anchors[anchors.length - 1];
      if (!a) return;
      expect(a.download).toMatch(/architecture-job-abc\.mmd/);
    });

    vi.restoreAllMocks();
  });

  it("triggers a TM-BOM download", async () => {
    const anchors: HTMLAnchorElement[] = [];
    const origCreate = document.createElement.bind(document);
    vi.spyOn(document, "createElement").mockImplementation((tag: string) => {
      const el = origCreate(tag);
      if (tag === "a") anchors.push(el as HTMLAnchorElement);
      return el;
    });

    renderPanel({ systemSummary: "System" });
    await userEvent.click(screen.getByRole("button", { name: /download tm-bom/i }));

    await waitFor(() => {
      expect(anchors.length).toBeGreaterThan(0);
      const a = anchors[anchors.length - 1];
      if (!a) return;
      expect(a.download).toMatch(/tm-bom-job-abc\.json/);
    });

    vi.restoreAllMocks();
  });

  it("triggers a Threat Dragon v2 download", async () => {
    const anchors: HTMLAnchorElement[] = [];
    const origCreate = document.createElement.bind(document);
    vi.spyOn(document, "createElement").mockImplementation((tag: string) => {
      const el = origCreate(tag);
      if (tag === "a") anchors.push(el as HTMLAnchorElement);
      return el;
    });

    renderPanel({ systemSummary: "System" });
    await userEvent.click(screen.getByRole("button", { name: /download threat dragon v2/i }));

    await waitFor(() => {
      expect(anchors.length).toBeGreaterThan(0);
      const a = anchors[anchors.length - 1];
      if (!a) return;
      expect(a.download).toMatch(/threat-dragon-v2-job-abc\.json/);
    });

    vi.restoreAllMocks();
  });
});
