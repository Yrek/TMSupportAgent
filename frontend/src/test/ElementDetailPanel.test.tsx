/**
 * F-T09 — ElementDetailPanel unit tests
 *
 * ConfirmDialog and AddCorrectionModal are mocked to isolate panel logic.
 */
import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, it, expect, vi } from "vitest";
import { ElementDetailPanel } from "@/components/architecture/ElementDetailPanel";
import type {
  ArchitectureElement,
  CorrectElementRequest,
  PatchElementRequest,
} from "@/api/architecture";

// ── Mocks ─────────────────────────────────────────────────────────────────────

vi.mock("@/components/common/ConfirmDialog", () => ({
  ConfirmDialog: ({
    open,
    onConfirm,
    confirmLabel,
  }: {
    open: boolean;
    onConfirm: () => void | Promise<void>;
    confirmLabel: string;
  }) =>
    open ? (
      <div role="alertdialog">
        <button onClick={() => void onConfirm()}>{confirmLabel}</button>
      </div>
    ) : null,
}));

vi.mock("@/components/architecture/AddCorrectionModal", () => ({
  AddCorrectionModal: () => null,
}));

// ── Fixtures ──────────────────────────────────────────────────────────────────

const userAddedElement: ArchitectureElement = {
  id: "el-1",
  elementType: "Component",
  name: "Auth Service",
  description: "Handles authentication",
  properties: {},
  source: "UserAdded",
  extractionConfidence: null,
  createdAt: new Date().toISOString(),
  corrections: [],
};

const extractedElement: ArchitectureElement = {
  ...userAddedElement,
  id: "el-2",
  name: "Database",
  source: "Extracted",
  extractionConfidence: "High",
};

function renderPanel(
  element: ArchitectureElement,
  overrides: {
    readOnly?: boolean;
    onPatch?: (req: PatchElementRequest) => Promise<void>;
    onDelete?: () => Promise<void>;
    onCorrect?: (req: CorrectElementRequest) => Promise<void>;
  } = {},
) {
  const onPatch =
    overrides.onPatch ??
    vi.fn<(req: PatchElementRequest) => Promise<void>>().mockResolvedValue(undefined);
  const onDelete =
    overrides.onDelete ?? vi.fn<() => Promise<void>>().mockResolvedValue(undefined);
  const onCorrect =
    overrides.onCorrect ??
    vi.fn<(req: CorrectElementRequest) => Promise<void>>().mockResolvedValue(undefined);

  render(
    <ElementDetailPanel
      element={element}
      readOnly={overrides.readOnly ?? false}
      onPatch={onPatch}
      onDelete={onDelete}
      onCorrect={onCorrect}
    />,
  );

  return { onPatch, onDelete, onCorrect };
}

// ── Tests ─────────────────────────────────────────────────────────────────────

describe("ElementDetailPanel", () => {
  describe("display", () => {
    it("renders the element name", () => {
      renderPanel(userAddedElement);
      expect(screen.getByText("Auth Service")).toBeInTheDocument();
    });

    it("shows User Added badge for UserAdded source", () => {
      renderPanel(userAddedElement);
      expect(screen.getByText(/user added/i)).toBeInTheDocument();
    });

    it("shows Extracted badge for Extracted source", () => {
      renderPanel(extractedElement);
      expect(screen.getByText(/extracted/i)).toBeInTheDocument();
    });
  });

  describe("edit and save", () => {
    it("shows editable name field after clicking edit button", async () => {
      renderPanel(userAddedElement);
      await userEvent.click(screen.getByRole("button", { name: /edit element/i }));
      expect(screen.getByLabelText(/^name$/i)).toBeInTheDocument();
    });

    it("calls onPatch with updated name and description on save", async () => {
      const { onPatch } = renderPanel(userAddedElement);

      await userEvent.click(screen.getByRole("button", { name: /edit element/i }));

      const nameInput = screen.getByLabelText(/^name$/i);
      await userEvent.clear(nameInput);
      await userEvent.type(nameInput, "Updated Service");

      await userEvent.click(screen.getByRole("button", { name: /^save$/i }));

      await waitFor(() => {
        expect(onPatch).toHaveBeenCalledWith(
          expect.objectContaining({ name: "Updated Service" }),
        );
      });
    });
  });

  describe("delete (UserAdded elements only)", () => {
    it("shows delete button for UserAdded elements", () => {
      renderPanel(userAddedElement);
      expect(screen.getByRole("button", { name: /delete element/i })).toBeInTheDocument();
    });

    it("does not show delete button for Extracted elements", () => {
      renderPanel(extractedElement);
      expect(screen.queryByRole("button", { name: /delete element/i })).not.toBeInTheDocument();
    });

    it("shows confirmation dialog when delete button is clicked", async () => {
      renderPanel(userAddedElement);
      await userEvent.click(screen.getByRole("button", { name: /delete element/i }));
      expect(screen.getByRole("alertdialog")).toBeInTheDocument();
    });

    it("calls onDelete when deletion is confirmed", async () => {
      const { onDelete } = renderPanel(userAddedElement);
      await userEvent.click(screen.getByRole("button", { name: /delete element/i }));
      await userEvent.click(screen.getByRole("button", { name: /^delete$/i }));

      await waitFor(() => {
        expect(onDelete).toHaveBeenCalledOnce();
      });
    });
  });

  describe("readOnly mode", () => {
    it("does not render edit or delete buttons in readOnly mode", () => {
      renderPanel(userAddedElement, { readOnly: true });
      expect(screen.queryByRole("button", { name: /edit element/i })).not.toBeInTheDocument();
      expect(screen.queryByRole("button", { name: /delete element/i })).not.toBeInTheDocument();
    });
  });
});
