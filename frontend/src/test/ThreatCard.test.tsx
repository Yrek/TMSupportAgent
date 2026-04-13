import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, it, expect, vi } from "vitest";
import { MemoryRouter } from "react-router-dom";
import { ThreatCard } from "@/components/threats/ThreatCard";
import type { Threat } from "@/api/threats";

const mockThreat: Threat = {
  id: "t1",
  identifier: "T-001",
  title: "SQL Injection via user input",
  methodCategory: "STRIDE",
  affectedElementIds: ["el1"],
  description: "An attacker can inject SQL via the login form.",
  attackScenario: "Attacker submits malicious SQL.",
  preconditions: null,
  impactedAssets: ["User data"],
  securityImpact: "Data breach",
  privacyImpact: null,
  existingControls: null,
  controlGaps: null,
  confidence: "High",
  evidenceBasis: [],
  evidenceStrength: "Direct",
  assumptions: null,
  findingType: "Confirmed",
  status: "Open",
  source: "System",
  mitigations: [],
  frameworkMappings: [],
  notes: [],
};

describe("ThreatCard", () => {
  it("renders threat identifier and title", () => {
    render(
      <MemoryRouter>
        <ThreatCard threat={mockThreat} onClick={vi.fn()} />
      </MemoryRouter>,
    );
    expect(screen.getByText("T-001")).toBeInTheDocument();
    expect(screen.getByText("SQL Injection via user input")).toBeInTheDocument();
  });

  it("calls onClick callback when clicked", async () => {
    const onClick = vi.fn();
    render(
      <MemoryRouter>
        <ThreatCard threat={mockThreat} onClick={onClick} />
      </MemoryRouter>,
    );
    await userEvent.click(screen.getByText("T-001").closest("button")!);
    expect(onClick).toHaveBeenCalledWith(mockThreat);
  });
});
