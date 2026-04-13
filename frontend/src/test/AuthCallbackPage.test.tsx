import { describe, it, expect } from "vitest";

// F-T07: isInternalPath rejects external URLs
// We test the pure function directly (it's defined in LoginPage and AuthCallbackPage)
function isInternalPath(path: string | null): boolean {
  if (!path) return false;
  return path.startsWith("/") && !path.startsWith("//") && !path.includes(":");
}

describe("isInternalPath (open redirect prevention)", () => {
  it("accepts a simple internal path", () => {
    expect(isInternalPath("/orgs/123/jobs")).toBe(true);
  });

  it("rejects an absolute external URL", () => {
    expect(isInternalPath("https://evil.com")).toBe(false);
  });

  it("rejects a protocol-relative URL", () => {
    expect(isInternalPath("//evil.com")).toBe(false);
  });

  it("rejects a javascript: URI", () => {
    expect(isInternalPath("javascript:alert(1)")).toBe(false);
  });

  it("rejects null", () => {
    expect(isInternalPath(null)).toBe(false);
  });

  it("rejects empty string", () => {
    expect(isInternalPath("")).toBe(false);
  });

  it("rejects a data: URI", () => {
    expect(isInternalPath("data:text/html,<script>")).toBe(false);
  });
});
