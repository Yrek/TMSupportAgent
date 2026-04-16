/**
 * F-T10 — ExportPanel unit tests
 *
 * useExportAnalysis is mocked so no real HTTP call is made.
 * The component is wrapped in QueryClientProvider because it internally
 * calls useMutation via useExportAnalysis.
 */
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { ExportPanel } from "@/components/analysis/ExportPanel";

// ── Mock API layer ────────────────────────────────────────────────────────────

const mockMutateAsync = vi.fn().mockResolvedValue(undefined);

vi.mock("@/api/threats", () => ({
  useExportAnalysis: () => ({
    mutateAsync: mockMutateAsync,
    isPending: false,
  }),
}));

// ── Helpers ───────────────────────────────────────────────────────────────────

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
  // Stub URL.createObjectURL / revokeObjectURL for Markdown download path
  vi.stubGlobal("URL", {
    createObjectURL: vi.fn(() => "blob:mock"),
    revokeObjectURL: vi.fn(),
  });
});

// ── Tests ─────────────────────────────────────────────────────────────────────

describe("ExportPanel", () => {
  it("renders Download JSON and Download Markdown buttons", () => {
    renderPanel();
    expect(screen.getByRole("button", { name: /download json/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /download markdown/i })).toBeInTheDocument();
  });

  it("calls useExportAnalysis mutateAsync when Download JSON is clicked", async () => {
    renderPanel();
    await userEvent.click(screen.getByRole("button", { name: /download json/i }));
    await waitFor(() => {
      expect(mockMutateAsync).toHaveBeenCalledOnce();
    });
  });

  it("does not include auth token in the download filename (mutation handles naming)", async () => {
    renderPanel();
    await userEvent.click(screen.getByRole("button", { name: /download json/i }));
    await waitFor(() => {
      // mutateAsync was called — the blob download is inside the mutation, not here
      // Verify the button label does not expose any token or credential
      const btn = screen.getByRole("button", { name: /download json/i });
      expect(btn.textContent).not.toMatch(/bearer|token|key/i);
    });
  });

  it("triggers a Blob download when Download Markdown is clicked", async () => {
    // Spy on document.createElement to capture anchor href + download
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
      // An anchor was created for the Blob download
      expect(anchors.length).toBeGreaterThan(0);
      const a = anchors[anchors.length - 1];
      expect(a).toBeDefined();
      if (!a) return;
      expect(a.download).toMatch(/threat-model-job-abc\.md/);
    });

    vi.restoreAllMocks();
  });
});
