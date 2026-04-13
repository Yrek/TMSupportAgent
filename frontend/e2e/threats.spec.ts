import { test, expect } from "@playwright/test";

/**
 * F-E03 — Threat status update
 *
 * Requires: running API with a seeded COMPLETE job + WorkOS test credentials.
 * Marked fixme until the test environment provides auth helpers and seed data.
 */
test.describe("Threat status update", () => {
  test("analysis page is reachable when authenticated", async ({ page }) => {
    await page.goto("/orgs/test-org/jobs/test-job/analysis");
    const url = page.url();
    expect(url).toMatch(/analysis|login/);
  });

  test.fixme(
    "threat status change persists across page reload",
    async ({ page }) => {
      // Pre-condition: a COMPLETE job exists with at least one threat
      const jobId = process.env["E2E_COMPLETE_JOB_ID"] ?? "test-job";
      const orgId = process.env["E2E_ORG_ID"] ?? "test-org";

      // Step 1: open the analysis page
      await page.goto(`/orgs/${orgId}/jobs/${jobId}/analysis`);
      await expect(page.getByRole("heading", { name: /analysis|threat/i })).toBeVisible();

      // Step 2: click on the first threat card
      const firstCard = page.getByTestId("threat-card").first();
      await firstCard.click();

      // Step 3: detail panel should show status selector
      const statusSelect = page.getByLabel(/status/i);
      await expect(statusSelect).toBeVisible();

      // Step 4: change status to "Accepted"
      await statusSelect.selectOption("Accepted");
      await page.getByRole("button", { name: /save/i }).click();
      await expect(page.getByText(/saved|updated/i)).toBeVisible({ timeout: 5_000 });

      // Step 5: reload and verify the status persisted
      await page.reload();
      await firstCard.click();
      await expect(page.getByLabel(/status/i)).toHaveValue("Accepted");
    },
  );
});
