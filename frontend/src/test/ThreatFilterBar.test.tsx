import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, it, expect } from "vitest";
import { MemoryRouter } from "react-router-dom";
import { ThreatFilterBar } from "@/components/threats/ThreatFilterBar";

function Wrapper({ children }: { children: React.ReactNode }) {
  return <MemoryRouter>{children}</MemoryRouter>;
}

describe("ThreatFilterBar", () => {
  it("renders filter buttons", () => {
    render(
      <Wrapper>
        <ThreatFilterBar />
      </Wrapper>,
    );
    expect(screen.getByText("Confirmed")).toBeInTheDocument();
    expect(screen.getByText("Open")).toBeInTheDocument();
    expect(screen.getByText("High")).toBeInTheDocument();
  });

  it("toggles a filter on click and shows Clear all", async () => {
    render(
      <Wrapper>
        <ThreatFilterBar />
      </Wrapper>,
    );
    await userEvent.click(screen.getByText("Confirmed"));
    expect(screen.getByText("Clear all")).toBeInTheDocument();
  });
});
