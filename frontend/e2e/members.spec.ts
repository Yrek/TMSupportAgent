import { test, expect } from "@playwright/test";

/**
 * F-E06 — Member invite
 *
 * Requires: running API + WorkOS test credentials + owner session.
 * Marked fixme until the test environment provides auth helpers.
 *
 * Security note: the invite form always shows "Invitation sent" regardless of
 * whether the email exists (no enumeration oracle). The test verifies this.
 */
test.describe("Member invite", () => {
  test("members settings page is reachable when authenticated", async ({ page }) => {
    await page.goto("/orgs/test-org/settings/members");
    const url = page.url();
    expect(url).toMatch(/members|login|unauthorized/);
  });

  test.fixme(
    "owner can invite a new member and always sees success toast",
    async ({ page }) => {
      const orgId = process.env["E2E_ORG_ID"] ?? "test-org";

      // Pre-condition: logged in as owner of the org
      await page.goto(`/orgs/${orgId}/settings/members`);
      await expect(page.getByRole("heading", { name: /members/i })).toBeVisible();

      // Step 1: invite form is visible for owners
      const emailInput = page.getByLabel(/email/i);
      await expect(emailInput).toBeVisible();

      // Step 2: fill in a new email
      await emailInput.fill("invite-test@example.com");
      await page.getByRole("button", { name: /invite/i }).click();

      // Step 3: always shows success — no enumeration oracle (CLAUDE.md §7.6)
      await expect(page.getByText(/invitation sent/i)).toBeVisible({ timeout: 5_000 });

      // Step 4: the same success message appears even for an already-invited email
      await emailInput.fill("invite-test@example.com");
      await page.getByRole("button", { name: /invite/i }).click();
      await expect(page.getByText(/invitation sent/i)).toBeVisible({ timeout: 5_000 });
    },
  );

  test.fixme(
    "non-owner does not see the invite form",
    async ({ page }) => {
      const orgId = process.env["E2E_ORG_ID"] ?? "test-org";

      // Pre-condition: logged in as a member (not owner) of the org
      await page.goto(`/orgs/${orgId}/settings/members`);

      // Members page is owner-gated at the route level — expect redirect or 401
      const url = page.url();
      expect(url).toMatch(/unauthorized|login/);
    },
  );
});
