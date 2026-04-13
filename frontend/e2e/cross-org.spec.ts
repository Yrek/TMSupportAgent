import { test, expect } from "@playwright/test";

/**
 * F-E04 — Cross-org isolation: user in org A cannot navigate to org B's job URL.
 *
 * Requires: two authenticated orgs A and B, with a known job in org B.
 */
test.describe("Cross-org isolation", () => {
  test.skip("user cannot access another org's job", async ({ page }) => {
    // TODO: sign in as org A user, attempt to navigate to org B job URL
    await page.goto("/orgs/org-b-id/jobs/job-b-id");
    // Expect either 404 page or redirect back to own org
    await expect(page).not.toHaveURL(/org-b-id/);
  });
});
