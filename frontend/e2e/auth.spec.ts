import { test, expect } from "@playwright/test";

/**
 * F-E05 — Auth: unauthenticated navigation to protected route redirects to login
 * with return_to; after login, lands on correct page.
 *
 * Note: Full E2E requires a running API + WorkOS test credentials.
 * This stub defines the test structure; update beforeEach with auth helpers as needed.
 */
test.describe("Authentication", () => {
  test("unauthenticated navigation redirects to login with return_to param", async ({ page }) => {
    await page.goto("/orgs/test-org/jobs");
    await expect(page).toHaveURL(/\/login/);
    const url = new URL(page.url());
    expect(url.searchParams.has("return_to")).toBeTruthy();
  });

  test("login page is accessible without authentication", async ({ page }) => {
    await page.goto("/login");
    await expect(page).toHaveURL(/\/login/);
  });
});
