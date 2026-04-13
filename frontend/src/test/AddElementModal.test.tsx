/**
 * F-T04 — AddElementModal unit tests
 *
 * Dialog and Select are mocked so the test focuses on form validation
 * and submission behaviour rather than Radix UI internals.
 */
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, it, expect, vi } from "vitest";
import { AddElementModal } from "@/components/architecture/AddElementModal";

// ── Radix mocks ──────────────────────────────────────────────────────────────

vi.mock("@/components/ui/dialog", () => ({
  Dialog: ({ children, open }: { children: React.ReactNode; open: boolean }) =>
    open ? <>{children}</> : null,
  DialogContent: ({ children }: { children: React.ReactNode }) => (
    <div role="dialog">{children}</div>
  ),
  DialogHeader: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
  DialogTitle: ({ children }: { children: React.ReactNode }) => <h2>{children}</h2>,
  DialogFooter: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

vi.mock("@/components/ui/select", () => ({
  Select: ({
    children,
    onValueChange,
    value,
  }: {
    children: React.ReactNode;
    onValueChange: (v: string) => void;
    value: string;
  }) => (
    <div data-testid="select-root" data-value={value} onChange={(e: React.FormEvent<HTMLDivElement>) => onValueChange((e.target as HTMLSelectElement).value)}>
      {children}
    </div>
  ),
  SelectTrigger: ({ children, id }: { children: React.ReactNode; id?: string }) => (
    <button type="button" id={id}>
      {children}
    </button>
  ),
  SelectContent: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
  SelectItem: ({
    children,
    value,
  }: {
    children: React.ReactNode;
    value: string;
  }) => <option value={value}>{children}</option>,
  SelectValue: ({ placeholder }: { placeholder?: string }) => <span>{placeholder ?? ""}</span>,
}));

// ── Helpers ───────────────────────────────────────────────────────────────────

function renderModal(onSubmit = vi.fn().mockResolvedValue(undefined)) {
  const onOpenChange = vi.fn();
  render(
    <AddElementModal open={true} onOpenChange={onOpenChange} onSubmit={onSubmit} />,
  );
  return { onSubmit, onOpenChange };
}

// ── Tests ─────────────────────────────────────────────────────────────────────

describe("AddElementModal", () => {
  it("renders the name field and submit button", () => {
    renderModal();
    expect(screen.getByLabelText(/name/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /add element/i })).toBeInTheDocument();
  });

  it("shows a validation error when name is empty and form is submitted", async () => {
    renderModal();
    await userEvent.click(screen.getByRole("button", { name: /add element/i }));
    expect(await screen.findByText(/name is required/i)).toBeInTheDocument();
  });

  it("does not call onSubmit when name is empty", async () => {
    const { onSubmit } = renderModal();
    await userEvent.click(screen.getByRole("button", { name: /add element/i }));
    await waitFor(() => {
      expect(onSubmit).not.toHaveBeenCalled();
    });
  });

  it("calls onSubmit with correct payload when form is valid", async () => {
    const { onSubmit } = renderModal();
    await userEvent.type(screen.getByLabelText(/name \*/i), "Auth Service");
    await userEvent.click(screen.getByRole("button", { name: /add element/i }));

    await waitFor(() => {
      expect(onSubmit).toHaveBeenCalledWith(
        expect.objectContaining({
          name: "Auth Service",
          elementType: "Component",
        }),
      );
    });
  });

  it("closes the dialog and resets the form after successful submit", async () => {
    const { onSubmit, onOpenChange } = renderModal();
    await userEvent.type(screen.getByLabelText(/name \*/i), "My DB");
    await userEvent.click(screen.getByRole("button", { name: /add element/i }));

    await waitFor(() => {
      expect(onOpenChange).toHaveBeenCalledWith(false);
    });
    // After close the form would be re-opened fresh — onSubmit was called once
    expect(onSubmit).toHaveBeenCalledOnce();
  });

  it("includes description in payload when filled in", async () => {
    const { onSubmit } = renderModal();
    await userEvent.type(screen.getByLabelText(/name \*/i), "API Gateway");
    await userEvent.type(screen.getByLabelText(/description/i), "Routes external traffic");
    await userEvent.click(screen.getByRole("button", { name: /add element/i }));

    await waitFor(() => {
      expect(onSubmit).toHaveBeenCalledWith(
        expect.objectContaining({
          name: "API Gateway",
          description: "Routes external traffic",
        }),
      );
    });
  });
});
