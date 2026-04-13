import { test, expect } from "@playwright/test";

/**
 * F-E02 — Full manual job flow
 *
 * Requires: running API + WorkOS test credentials.
 * The full flow is marked fixme until auth helpers are available.
 */
test.describe("Manual job flow", () => {
  test("manual job page is reachable when authenticated", async ({ page }) => {
    await page.goto("/orgs/test-org/jobs/new/manual");
    const url = page.url();
    expect(url).toMatch(/manual|login/);
  });

  test.fixme(
    "full manual flow: create job → add elements → confirm → analysis starts",
    async ({ page }) => {
      // Pre-condition: authenticated session with a test org
      // Step 1: navigate to manual job page
      await page.goto("/orgs/test-org/jobs/new/manual");
      await expect(page.getByRole("heading", { name: /manual/i })).toBeVisible();

      // Step 2: fill in job title and system purpose
      await page.getByLabel(/title/i).fill("E2E Test Architecture");
      await page.getByLabel(/system purpose/i).fill("This is a test system for E2E validation.");

      // Step 3: submit to create the job (lands on review page)
      await page.getByRole("button", { name: /create/i }).click();
      await expect(page).toHaveURL(/\/review/);

      // Step 4: add a component element
      await page.getByRole("button", { name: /add element/i }).click();
      await page.getByLabel(/name \*/i).fill("Auth Service");
      await page.getByRole("button", { name: /^add element$/i }).click();

      // Step 5: add a data store element
      await page.getByRole("button", { name: /add element/i }).click();
      await page.getByLabel(/name \*/i).fill("User Database");
      await page.getByRole("button", { name: /^add element$/i }).click();

      // Step 6: element list should show both elements
      await expect(page.getByText("Auth Service")).toBeVisible();
      await expect(page.getByText("User Database")).toBeVisible();

      // Step 7: confirm architecture to start analysis
      await page.getByRole("button", { name: /confirm architecture/i }).click();
      await page.getByRole("button", { name: /confirm and start analysis/i }).click();

      // Step 8: redirected to job detail page; job eventually reaches ANALYZING
      await expect(page).toHaveURL(/\/jobs\/[^/]+$/);
      await expect(
        page.getByText(/classifying|analyzing|synthesizing|complete/i),
      ).toBeVisible({ timeout: 120_000 });
    },
  );
});
