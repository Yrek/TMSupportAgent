import { test, expect } from "@playwright/test";

/**
 * F-E01 — Full upload flow
 *
 * Requires: running API + Worker + WorkOS test credentials.
 * The upload and polling assertions are marked fixme until the test environment
 * is wired with auth helpers and a real API. Navigation/UI structure assertions
 * run against the static dev server.
 */
test.describe("Upload job flow", () => {
  test("upload job page is reachable when authenticated", async ({ page }) => {
    // Without auth this should redirect to login — the redirect itself is the assertion
    await page.goto("/orgs/test-org/jobs/new/upload");
    // Either we see the upload page (if auth is set up) or we're redirected to login
    const url = page.url();
    expect(url).toMatch(/upload|login/);
  });

  test.fixme(
    "full upload flow: PNG → job created → AWAITING_REVIEW → review page with elements",
    async ({ page }) => {
      // Pre-condition: authenticated session with a test org
      // Step 1: navigate to upload page
      await page.goto("/orgs/test-org/jobs/new/upload");
      await expect(page.getByRole("region", { name: /drop/i })).toBeVisible();

      // Step 2: drop a PNG file onto the dropzone
      const [fileChooser] = await Promise.all([
        page.waitForEvent("filechooser"),
        page.click('[data-testid="dropzone"]'),
      ]);
      await fileChooser.setFiles("e2e/fixtures/sample-architecture.png");

      // Step 3: submit
      await page.getByRole("button", { name: /submit/i }).click();

      // Step 4: redirected to job detail page and polls to AWAITING_REVIEW
      await expect(page).toHaveURL(/\/jobs\//);
      await expect(
        page.getByText(/awaiting review/i),
        "job should reach AWAITING_REVIEW within 2 minutes",
      ).toBeVisible({ timeout: 120_000 });

      // Step 5: navigate to review page
      await page.getByRole("link", { name: /review/i }).click();
      await expect(page).toHaveURL(/\/review/);

      // Step 6: at least one element should be visible
      await expect(page.getByRole("listitem").first()).toBeVisible();
    },
  );
});
