import { test, expect } from "@playwright/test";

/**
 * F-E07 — Export: analysis page export tab → "Download JSON" calls `/export`
 * (not `/analysis`) → file downloaded with Content-Disposition filename.
 *
 * Requires: authenticated session + a job in Complete status.
 */
test.describe("Export", () => {
  test.skip("Download JSON calls /export endpoint with correct filename", async ({ page }) => {
    // TODO: set up authenticated session and navigate to a complete job's analysis page
    // Intercept the /export request and verify it is NOT /analysis
    const [download] = await Promise.all([
      page.waitForEvent("download"),
      page.click('[data-testid="download-json"]'),
    ]);
    expect(download.suggestedFilename()).toMatch(/^threat-model-.+\.json$/);
  });
});
