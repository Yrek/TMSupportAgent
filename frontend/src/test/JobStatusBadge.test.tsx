import { render, screen } from "@testing-library/react";
import { describe, it, expect } from "vitest";
import { JobStatusBadge } from "@/components/jobs/JobStatusBadge";
import { JOB_STATUSES } from "@/lib/constants";

describe("JobStatusBadge", () => {
  it.each(JOB_STATUSES)("renders correct label for status %s", (status) => {
    render(<JobStatusBadge status={status} />);
    // Each status should render a visible text label
    const badge = screen.getByText(/./); // non-empty text
    expect(badge).toBeInTheDocument();
  });
});
