# Frontend Specification

**Status:** Draft  
**Spec refs:** [01-product.md](01-product.md), [02-architecture.md](02-architecture.md), [06-security.md](06-security.md), [CLAUDE.md](../../CLAUDE.md)  
**API ref:** [openapi.yaml](../api/openapi.yaml)  
**Version:** 0.2  
**Date:** 2026-04-11

---

## 1. Scope

This specification covers the browser SPA (single-page application) that is the primary user interface for the Threat Modeling Agent platform.

It defines:
- technology selection and rationale
- application structure and routing
- all screens and their required interactions
- API integration contract
- authentication and session handling
- security requirements for the frontend itself
- state management approach
- polling strategy (SSE deferred per OD-3)
- a detailed implementation backlog

This document does **not** cover:
- backend API contracts (see [openapi.yaml](../api/openapi.yaml))
- deployment infrastructure (see [azure.md](../deployment/azure.md))
- prompt templates or analysis logic

---

## 2. Technology Selection

### 2.1 Framework: React + Vite

| Decision | Choice | Rationale |
|---|---|---|
| SPA framework | **React 19** | Mature ecosystem, strong TypeScript support, large talent pool |
| Build tool | **Vite 6** | Sub-second HMR, native ESM, excellent for SPA |
| Language | **TypeScript (strict)** | Required — catches API contract drift; no `any` without justified comment |
| Routing | **React Router v7** | Nested routes, loader pattern, good code-splitting |
| Server state | **TanStack Query v5** | Cache-aware data fetching, polling support, mutation tracking |
| Forms | **React Hook Form + Zod** | Performant, schema-validated forms; Zod schemas shared with API type layer |
| Styling | **Tailwind CSS v4** | Utility-first, no CSS-in-JS runtime overhead |
| UI primitives | **shadcn/ui** | Accessible, unstyled, composable; built on Radix UI |
| Diagram rendering | **React Flow** | Interactive node/edge graph for architecture canvas |
| File upload | **react-dropzone** | Drag-and-drop with MIME/extension validation before upload |
| Icons | **Lucide React** | Consistent, tree-shakable |
| Auth SDK | **WorkOS AuthKit React** | Official React SDK for WorkOS authentication |
| HTTP client | **axios** | Interceptor support for auth token injection and 401 refresh |
| Toasts | **sonner** | Non-blocking notifications for job status changes |
| Date formatting | **date-fns** | Lightweight, tree-shakable |
| Testing | **Vitest + React Testing Library + Playwright** | Unit/component + E2E |
| Linting | **ESLint (flat config) + Prettier** | Consistent formatting, strict rules |
| Package manager | **pnpm** | Fast, strict, workspace-compatible |

### 2.2 Deployment target

Deployed to **Azure Static Web Apps (Standard tier)** as specified in `02-architecture.md §4.1`. The SPA shell is static; all data comes from the API.

### 2.3 Not chosen and why

| Alternative | Rejected reason |
|---|---|
| Next.js | Server-side rendering adds deployment complexity to Azure Static Web Apps; API already handles all data; SSR not required for this app |
| Vue / Svelte | Smaller ecosystems for this domain; team familiarity |
| Redux | Overkill with TanStack Query handling server state; Zustand for client-only state if needed |
| GraphQL | Not warranted; REST API already defined |
| MUI / Chakra | Heavy runtime; shadcn/ui gives better control |

---

## 3. Security Requirements (Frontend)

These are in addition to CLAUDE.md. The frontend is a browser SPA — its threat surface differs from the backend.

### 3.1 Authentication

- The SPA MUST integrate WorkOS AuthKit React for the authentication flow.
- Access tokens MUST be stored in memory only — never in `localStorage`, `sessionStorage`, or cookies accessible to JavaScript.
- WorkOS handles the OAuth/OIDC flow; the SPA receives a short-lived token from WorkOS AuthKit.
- Silent token refresh MUST be implemented via WorkOS AuthKit's built-in mechanism.
- On 401 from the API, the SPA MUST attempt one silent refresh then redirect to login if it fails.
- On sign-out, the in-memory token MUST be cleared and the user redirected to the login screen.

### 3.2 Token handling

- The Bearer token MUST be attached to API requests by a single axios interceptor — never inline in components.
- The token MUST NOT be logged, rendered, or put in URL parameters.
- The token MUST NOT be passed to any third-party service.

### 3.3 CSP and headers

- The SPA is served from Azure Static Web Apps, which allows `staticwebapp.config.json` to set response headers.
- The following headers MUST be set in `staticwebapp.config.json`:
  - `Content-Security-Policy`: restrict `default-src`, allow only the API origin and WorkOS; no `unsafe-inline` for scripts; `nonce`-based or hash-based for any inline styles only
  - `X-Frame-Options: DENY`
  - `X-Content-Type-Options: nosniff`
  - `Referrer-Policy: strict-origin-when-cross-origin`
  - `Permissions-Policy: camera=(), microphone=(), geolocation=(), payment=()`
  - `Strict-Transport-Security: max-age=0` (staged rollout — increase to full value before GA per OPS-13)
- `Server` and `X-Powered-By` headers are absent from Static Web Apps by default.

### 3.4 Input and output handling

- All user input submitted to the API goes through Zod validation before the request is made.
- API responses MUST NOT be rendered as raw HTML (no `dangerouslySetInnerHTML` with API data).
- File uploads MUST be validated client-side by extension AND MIME type before submission. This is defense-in-depth only — server validation is the authority.
- Maximum file size MUST be checked client-side (10 MB) before upload to provide immediate feedback. Server limit is authoritative.
- Org ID and Job ID MUST come from URL params or API responses — never constructed client-side from user text.

### 3.5 CSRF

- The SPA uses Bearer tokens (not cookies) — no CSRF risk for API calls.
- WorkOS handles the OIDC state parameter for its own callback.

### 3.6 Open redirect prevention

- After login, redirect destinations MUST be validated against internal paths only.
- The login callback MUST NOT follow external redirect targets.

### 3.7 Dependency management

- All frontend dependencies MUST be pinned to exact versions in `package.json`.
- `pnpm-lock.yaml` MUST be committed.
- `pnpm audit` MUST run in CI and fail on high/critical CVEs.

---

## 4. Application Structure

```
frontend/
├── public/                     # Static assets (favicon, robots.txt)
├── src/
│   ├── api/                    # Axios client + TanStack Query hooks
│   │   ├── client.ts           # Axios instance with auth interceptor
│   │   ├── auth.ts             # Session, sign-out
│   │   ├── orgs.ts             # Org queries and mutations
│   │   ├── members.ts          # Member queries and mutations
│   │   ├── idp.ts              # IDP config queries and mutations
│   │   ├── jobs.ts             # Job queries, mutations, polling
│   │   ├── architecture.ts     # Architecture queries and mutations
│   │   ├── threats.ts          # Threat queries and mutations
│   │   └── me.ts               # Current user queries and mutations
│   ├── components/
│   │   ├── ui/                 # shadcn/ui base components
│   │   ├── layout/             # AppShell, Sidebar, TopNav, OrgSwitcher
│   │   ├── jobs/               # JobCard, JobStatusBadge, JobList, UploadDropzone
│   │   ├── architecture/       # ArchCanvas, ElementNode, ElementPanel, ElementForm
│   │   ├── threats/            # ThreatCard, ThreatList, ThreatStatusBadge, ThreatDetail
│   │   └── common/             # ConfirmDialog, ErrorBoundary, EmptyState, Spinner
│   ├── hooks/                  # Custom hooks (useCurrentOrg, useJobPoller, etc.)
│   ├── lib/                    # Zod schemas, formatters, constants
│   ├── pages/                  # Route components (one file per route)
│   │   ├── auth/               # Login, Callback
│   │   ├── dashboard/          # Dashboard (job list)
│   │   ├── jobs/               # JobSubmit, JobDetail, JobReview, JobAnalysis
│   │   ├── settings/           # OrgSettings, Members, IdpConfig, Profile
│   │   └── errors/             # NotFound, Unauthorized, Error
│   ├── router.tsx              # React Router route definitions
│   ├── main.tsx                # App entry point
│   └── types/                  # Generated or handwritten API types
├── e2e/                        # Playwright E2E tests
├── staticwebapp.config.json    # Azure Static Web Apps routing + security headers
├── vite.config.ts
├── tailwind.config.ts
├── tsconfig.json
├── .eslintrc.json
└── package.json
```

---

## 5. Routes

All routes except `/login` and `/auth/callback` require authentication. Unauthenticated access redirects to `/login` with the intended path as a validated internal `return_to` parameter.

| Path | Component | Description |
|---|---|---|
| `/login` | `LoginPage` | Sign-in screen — WorkOS AuthKit widget |
| `/auth/callback` | `AuthCallbackPage` | WorkOS OIDC callback handler |
| `/` | redirect | Redirect to `/orgs/{firstOrgId}/jobs` or org picker if multiple orgs |
| `/orgs` | `OrgPickerPage` | Choose organisation (or see no-access state if user has no mapped orgs) |
| `/orgs/:orgId/jobs` | `DashboardPage` | Job list — main landing page for an org |
| `/orgs/:orgId/jobs/new` | `SubmitJobPage` | Choose: upload file or draw manually |
| `/orgs/:orgId/jobs/new/upload` | `UploadJobPage` | Drag-and-drop file upload with title |
| `/orgs/:orgId/jobs/new/manual` | `ManualJobPage` | Draw architecture manually |
| `/orgs/:orgId/jobs/:jobId` | `JobDetailPage` | Status, progress, link to review or results |
| `/orgs/:orgId/jobs/:jobId/review` | `ReviewPage` | Architecture review and correction canvas |
| `/orgs/:orgId/jobs/:jobId/analysis` | `AnalysisPage` | Threat model results with threat list and diagram |
| `/orgs/:orgId/settings` | `OrgSettingsPage` | Organisation name, danger zone (delete) |
| `/orgs/:orgId/settings/members` | `MembersPage` | Member list, invite, role management |
| `/orgs/:orgId/settings/idp` | `IdpConfigPage` | Enterprise SSO configuration (owner only) |
| `/me` | `ProfilePage` | Current user — view profile, delete account |
| `*` | `NotFoundPage` | 404 |

---

## 6. Screens — Detailed Requirements

### 6.1 Login (`/login`)

- Render WorkOS AuthKit UI component (hosted or embedded).
- Show platform name and brief description.
- On successful authentication: extract token, store in memory, redirect to `return_to` (validated as internal path only) or `/`.
- No username/password fields built in the SPA — WorkOS handles all credential flows.

### 6.2 Dashboard (`/orgs/:orgId/jobs`)

**Purpose:** Primary workspace. Shows all jobs for the org with quick-action links.

**Requirements:**
- List jobs paginated (page size 20, cursor-based).
- Status filter (all / in-progress / awaiting review / complete / failed).
- Each job card shows: title (or "Untitled"), status badge, artifact type or "manual", created date, completed date if available.
- Status badges use semantic color: in-progress (blue), awaiting review (amber), complete (green), failed (red), partial (orange).
- Click a job card → navigate to appropriate page:
  - `AWAITING_REVIEW` → `/jobs/:jobId/review`
  - `COMPLETE` or `PARTIAL` → `/jobs/:jobId/analysis`
  - Otherwise → `/jobs/:jobId` (status/progress page)
- "New analysis" button → `/jobs/new`
- Jobs in `PENDING`, `PARSING`, `NORMALIZING`, `CLASSIFYING`, `ANALYZING`, `SYNTHESIZING` status MUST auto-refresh every 10 seconds (polling via TanStack Query `refetchInterval`).
- Empty state with clear call to action.
- Delete job action (confirm dialog): disabled if job is in-progress.

### 6.3 Submit Job (`/orgs/:orgId/jobs/new`)

**Purpose:** Entry point — choose upload or manual.

**Requirements:**
- Two large options: "Upload architecture file" and "Draw manually".
- Brief description of each path.
- Upload path → `/jobs/new/upload`.
- Manual path → `/jobs/new/manual`.

### 6.4 Upload Job (`/orgs/:orgId/jobs/new/upload`)

**Purpose:** File upload with title.

**Requirements:**
- Drag-and-drop zone accepting: `.png`, `.jpg`, `.jpeg`, `.gif`, `.webp`, `.puml`, `.txt`, `.md`, `.mmd`, `.drawio`, `.xml`.
- Client-side validation: extension must be in allowlist, file size ≤ 10 MB. Show error immediately if violated.
- File type icons and a help tooltip listing all supported formats.
- Optional title field (max 255 chars).
- Submit → `POST /orgs/:orgId/jobs` (multipart/form-data).
- On 202 → redirect to `/jobs/:jobId` (progress page).
- On 413 → show "File is too large (max 10 MB)".
- On 415 → show "File type not supported".
- On 429 → show "Too many submissions — try again shortly".
- Show upload progress indicator.

### 6.5 Manual Job (`/orgs/:orgId/jobs/new/manual`)

**Purpose:** Create a job with no file upload; draw the architecture from scratch on a canvas.

**Requirements:**
- Optional title field and optional system purpose textarea.
- Submit creates the job (`POST /orgs/:orgId/jobs/manual`) → navigates to `/jobs/:jobId/review` immediately (job is already in `AWAITING_REVIEW`).
- The review screen then drives element creation.

### 6.6 Job Progress (`/orgs/:orgId/jobs/:jobId`)

**Purpose:** Show job status for jobs that are still running.

**Requirements:**
- Show job title, current status, artifact type, created time.
- Progress stepper showing pipeline stages: Pending → Parsing → Normalizing → Awaiting Review → Classifying → Analyzing → Synthesizing → Complete.
- Current stage highlighted; completed stages checked.
- For `AWAITING_REVIEW`: show prominent CTA "Review architecture →" linking to `/review`.
- For `COMPLETE` / `PARTIAL`: show CTA "View threat model →" linking to `/analysis`.
- For `FAILED`: show error code, retry not automatic.
- Polling every 10 seconds while status is in-progress.
- On transition to `AWAITING_REVIEW` while user is watching: show toast "Architecture ready for review" and navigate automatically.
- On transition to `COMPLETE` while user is watching: show toast "Analysis complete" and navigate automatically.

### 6.7 Review Page (`/orgs/:orgId/jobs/:jobId/review`)

**Purpose:** The core pre-analysis step. User reviews the extracted (or empty for manual) architecture, corrects it, adds elements, then confirms to trigger threat analysis.

This is the most complex screen in the application.

**Layout:**
- Left panel: element list (scrollable, grouped by type).
- Centre: interactive canvas (`React Flow`) showing elements as nodes and data flows as edges.
- Right panel: selected element detail / edit form (shown when an element is selected, hidden otherwise).
- Top bar: job title, status chip, "Confirm architecture" button.

**Canvas requirements:**
- Each `ArchitectureElement` is a node. Node shape/color varies by `elementType`:
  - `Component` — rectangle, slate
  - `Actor` — circle, indigo
  - `DataFlow` — directed edge (not a node; rendered as React Flow edge between `from` and `to` elements)
  - `TrustBoundary` — dashed rectangle grouping contained components, teal
  - `DataStore` — cylinder, amber
  - `ExternalSystem` — rectangle with double border, violet
  - `Identity` — badge shape, pink
  - `BackgroundJob` — rounded rectangle with gear icon, orange
  - `LlmBoundary` — rectangle with AI icon, purple
- Node label = element name. Secondary label = type badge.
- Source badge on nodes: `extracted` (filled) vs `user_added` (outlined, different color).
- Confidence indicator for extracted elements: `high` (green dot), `medium` (yellow dot), `low` (red dot).
- Elements can be dragged to rearrange layout (layout is cosmetic only — not persisted to backend).
- Clicking a node → opens element detail panel on the right.
- Canvas supports zoom/pan.
- Auto-layout on first render (ELK or dagre via `@dagrejs/dagre`).

**Element detail panel (right):**
- Shown when an element is selected.
- Displays: name, type, description, source badge, confidence (if extracted), properties as key-value pairs (port, protocol, auth, trustZone, etc.).
- Edit mode (always editable for `AwaitingReview` jobs):
  - Name field (text, max 255).
  - Description field (textarea).
  - Properties: render known keys (port, protocol, auth, trustZone, technology, encryption) as labeled inputs; unknown keys as generic key/value pairs with delete button; "Add property" button for new key-value pairs.
  - Save button → `PATCH /orgs/:orgId/jobs/:jobId/elements/:elementId`.
- Delete button (user-added elements only, or any element for manual jobs) → `DELETE /orgs/:orgId/jobs/:jobId/elements/:elementId` with confirm dialog.
- "Corrections" section (extracted elements only): show list of corrections already recorded via the PATCH endpoint.

**Add element (manual and review mode):**
- Floating "+" button on canvas or "Add element" button in element list panel.
- Opens a modal/sheet: element type selector, name, description, properties.
- Submit → `POST /orgs/:orgId/jobs/:jobId}/elements`.
- New element appears on canvas.

**Architecture metadata panel (collapsible):**
- System purpose (read-only from API; editable for manual jobs before analysis).
- Assumptions list (from API — displayed as checklist with confirmed/unconfirmed state).
- Gaps list (displayed, not editable in MVP).
- Clarification questions (displayed with priority badge — high/medium/low).

**Confirm button:**
- Disabled until at least one element exists.
- Shows confirm dialog: "This will trigger threat analysis. You cannot make further corrections once confirmed."
- On confirm → `POST /orgs/:orgId/jobs/:jobId/architecture/confirm`.
- On success → navigate to `/jobs/:jobId` (progress page) with toast "Analysis started".

**Error states:**
- If job is not in `AWAITING_REVIEW` (e.g. already confirmed): show read-only view of the architecture with a note explaining status.

### 6.8 Analysis Page (`/orgs/:orgId/jobs/:jobId/analysis`)

**Purpose:** Display the completed threat model. This is the primary output screen.

**Layout:**
- Top: analysis summary header (system summary, classification chips, selected methods with rationale, model routing summary, threat counts).
- Tabs: Threats | Architecture | Recommendations | Remediation | Export.

**Threats tab:**
- Filter bar: finding type (confirmed / conditional / user-added), status (open / accepted / mitigated / rejected), confidence (high / medium / low), method category.
- Threat list — each card shows: identifier badge (e.g. T-001), title, method category, confidence badge, finding type badge, status badge, affected element names, short description excerpt.
- Clicking a threat → opens threat detail panel (or navigates to threat detail view on mobile).
- Threat detail panel shows all fields: full description, attack scenario, preconditions, impacted assets, security impact, privacy impact, existing controls, control gaps, evidence basis + strength, assumptions, mitigations list, framework mappings, notes.
- Status update: dropdown to change status (open / accepted / mitigated / rejected) with optional note → `PATCH /orgs/:orgId/jobs/:jobId/threats/:threatId/status`.
- Add note → `POST /orgs/:orgId/jobs/:jobId/threats/:threatId/notes`.
- "Add your own threat" button → opens form → `POST /orgs/:orgId/jobs/:jobId/threats`.

**Architecture tab (on Analysis page):**
- Same React Flow canvas as the review page, but **read-only**.
- Clicking an element shows threats mapped to it (filtered from the threats list).
- Threats are displayed as overlaid badges on nodes (count of threats, color-coded by highest severity).
- This satisfies spec §19 "interactive diagram requirements" — click element → see its threats.

**Recommendations tab:**
- List of secure design recommendations from analysis blob.
- Each item: title, description, mapped principles (NCSC / CLAUDE.md principles), affected elements.

**Remediation tab:**
- Prioritized remediation list from analysis blob.
- Grouped by priority: Critical / High / Medium / Low.
- Each item: threat identifier link, title, mitigation summary.

**Export tab:**
- Download JSON (full analysis blob from `GET /orgs/:orgId/jobs/:jobId/analysis`).
- Download Markdown report (client-side rendered from the analysis data).
- Note: CSV export of threats is listed as a future enhancement (§9.3 Deferred).

**Review questions:**
- If `reviewQuestions` array is non-empty, show a collapsible panel at the top of the Analysis page: "Questions requiring your review".

**Partial analysis:**
- If `status == PARTIAL`, show a prominent banner explaining that the analysis is incomplete due to architectural ambiguity.

**Re-analysis:**
- A "Re-analyze" button is shown when job status is COMPLETE or PARTIAL.
- Clicking shows a confirm dialog: "This will reset the architecture review and delete all system-generated threats. Your manually added threats will be preserved."
- On confirm → `POST /orgs/:orgId/jobs/:jobId/architecture/reanalyze`.
- On success (200 + JobDetail) → navigate to `/jobs/:jobId/review` with toast "Job reset for re-analysis".

### 6.9 Org Settings (`/orgs/:orgId/settings`)

- Org name field (owner only) → `PATCH /orgs/:orgId`.
- Danger zone: "Delete organisation" (owner only) — double-confirm dialog requiring org name to be typed → `DELETE /orgs/:orgId`.

### 6.10 Members (`/orgs/:orgId/settings/members`)

- Member list: avatar initials, display name (or email), role badge, joined date.
- Invite member (owner only): email + role picker → `POST /orgs/:orgId/members` → show "Invitation sent" regardless of whether user existed (no enumeration oracle).
- Role change (owner only): dropdown per member → `PATCH /orgs/:orgId/members/:userId`.
- Remove member (owner only): confirm dialog → `DELETE /orgs/:orgId/members/:userId`.
- Guard: cannot remove self if last owner. Show clear error from API 409 response.

### 6.11 IDP Config (`/orgs/:orgId/settings/idp`) — owner only

- Show current IDP config (provider type, domain hints) if configured.
- Form to set/update: provider type selector, domain hints (multi-value input), WorkOS connection ID field.
- Save → `PUT /orgs/:orgId/idp`.
- Delete IDP config → `DELETE /orgs/:orgId/idp` with confirm dialog.
- Note to user: the WorkOS connection must be created in the WorkOS dashboard before entering the connection ID here.

### 6.12 Profile (`/me`)

- Show user's internal ID and WorkOS user ID (no email — per CLAUDE.md §10.4 PII minimization; email is in WorkOS, not returned by `/me`).
- Account created date.
- "Delete account" — confirm dialog with "This is irreversible" warning → `DELETE /v1/me`.

### 6.13 Org Picker (`/orgs`)

- List of organisations user belongs to.
- Click → navigate to that org's dashboard.
- No self-service org creation in user plane. If user has no mapped organisations, show access guidance.
- Shown on first login or when user belongs to multiple orgs and navigates to `/`.

---

## 7. State Management

### 7.1 Server state: TanStack Query

All API data (jobs, architectures, threats, members, etc.) lives in TanStack Query cache. No manual state management for fetched data.

Key query keys:
```
['orgs']
['org', orgId]
['members', orgId]
['idp', orgId]
['jobs', orgId]
['jobs', orgId, { status }]
['job', orgId, jobId]
['architecture', orgId, jobId]
['elements', orgId, jobId]
['threats', orgId, jobId]
['threat', orgId, jobId, threatId]
['me']
```

Mutations use `onSuccess` callbacks to invalidate relevant query keys.

### 7.2 Polling

For active jobs, `useQuery` is called with:
```ts
refetchInterval: (data) => {
  const activeStatuses = ['Pending', 'Parsing', 'Normalizing', 'Classifying', 'Analyzing', 'Synthesizing'];
  return activeStatuses.includes(data?.status) ? 10_000 : false;
}
```

This satisfies OD-3 (polling vs SSE — polling chosen for MVP).

### 7.3 Client state: React context / Zustand

- Current org: `OrgContext` — tracks active `orgId`, available orgs, role in current org.
- Canvas layout: local component state (not persisted).
- Selected element on canvas: local component state.
- Active filters on threats page: URL search params (shareable links).
- UI state (sidebars, panels): local component state.

---

## 8. API Client Layer

### 8.1 Axios instance

```
src/api/client.ts
```

- Base URL from `VITE_API_BASE_URL` env variable.
- Request interceptor: attach `Authorization: Bearer <token>` from in-memory store.
- Response interceptor: on 401 → attempt silent token refresh → retry once → redirect to login.
- All responses are typed with Zod schemas to catch API contract drift early.

### 8.2 Type safety

Types generated from `openapi.yaml` using `openapi-typescript`. These are checked into source and regenerated when the API spec changes. No manual type duplication.

---

## 9. Error Handling

- 400 / 422: form-level validation errors shown inline.
- 403: show "You don't have permission for this action".
- 404: navigate to Not Found page.
- 409: show specific error message from `code` field (e.g. `JOB_IN_PROGRESS`, `ALREADY_CONFIRMED`, `LAST_OWNER`).
- 413: show "File too large".
- 415: show "File type not supported".
- 429: show "Too many requests — please wait a moment".
- 500 / network error: show toast with retry option; do not expose internal details.
- Global `ErrorBoundary` wraps the router to catch unexpected render errors.

---

## 10. Accessibility

- All interactive elements MUST have accessible labels (shadcn/ui + Radix UI provide this by default).
- Color is never the sole indicator of state — all status badges have text labels.
- Canvas (React Flow) is inherently non-accessible for keyboard navigation; a list-view fallback of all elements MUST be provided alongside the canvas.
- Focus management on modal open/close MUST follow ARIA patterns (Radix Dialog handles this).
- WCAG 2.1 AA is the target level.

---

## 11. Responsive Design

- Target: desktop-first (threat modeling is a professional desktop-focused workflow).
- Minimum supported width: 1024px. Below this, show a "best viewed on desktop" banner.
- Mobile: navigation collapses to a drawer. Canvas view shows element list only (no canvas). Analysis page shows threat list only (no diagram).
- Tablet: supported at ≥ 768px with simplified two-panel layout.

---

## 12. Implementation Backlog

Items are ordered by dependency. Do not start a group until all items marked as dependencies are complete.

---

### Group 0 — Project scaffold (no dependencies)

- [x] **F-000** — Initialise Vite + React 19 + TypeScript project with pnpm in `frontend/` directory
- [x] **F-001** — Configure `tsconfig.json` with `strict: true`, `noUncheckedIndexedAccess: true`, `exactOptionalPropertyTypes: true`
- [x] **F-002** — Configure ESLint flat config with `@typescript-eslint/strict`, `react-hooks`, `jsx-a11y`
- [x] **F-003** — Configure Prettier; wire lint + format to pre-commit hook via `husky` + `lint-staged`
- [x] **F-004** — Install and configure Tailwind CSS v4
- [x] **F-005** — Install and initialise `shadcn/ui` (Button, Input, Textarea, Select, Dialog, Sheet, Badge, Card, Tabs, Separator, Tooltip, DropdownMenu, Label)
- [x] **F-006** — Add `staticwebapp.config.json` with:
  - SPA routing fallback (`/*` → `/index.html`)
  - All required security headers (CSP, HSTS, X-Frame-Options, etc.) per §3.3
  - `Cache-Control: no-store` for all routes
- [x] **F-007** — Set up environment variable schema: `VITE_API_BASE_URL`, `VITE_WORKOS_CLIENT_ID`, `VITE_WORKOS_REDIRECT_URI`; fail build if any are missing (Zod parse at startup)
- [x] **F-008** — Add `openapi-typescript` codegen script; generate types from `docs/api/openapi.yaml` into `src/types/api.generated.ts`; add to CI
- [x] **F-009** — Configure Vitest with React Testing Library
- [x] **F-010** — Configure Playwright for E2E; add `e2e/` directory with smoke test placeholder
- [x] **F-011** — Add CI workflow: `pnpm install --frozen-lockfile` → lint → typecheck → unit tests → build → `pnpm audit --audit-level high`

---

### Group 1 — Auth foundation (depends on: Group 0)

- [x] **F-100** — Install `@workos-inc/authkit-react`; wrap app in `AuthKitProvider` with `VITE_WORKOS_CLIENT_ID`
- [x] **F-101** — Create `src/api/client.ts`: axios instance, base URL from env, request interceptor injecting Bearer token from WorkOS AuthKit `getAccessToken()`, response interceptor handling 401 with one retry after silent refresh then redirect to `/login`
- [x] **F-102** — Create `LoginPage` (`/login`): render WorkOS AuthKit sign-in component; handle `return_to` param (validate as internal path before use; reject external URLs)
- [x] **F-103** — Create `AuthCallbackPage` (`/auth/callback`): WorkOS OIDC callback handler; on success redirect to `return_to` or `/`
- [x] **F-104** — Create `RequireAuth` wrapper component: reads auth state from WorkOS AuthKit; if not authenticated, redirect to `/login?return_to=<current-path>` (validate path is internal)
- [x] **F-105** — Create `OrgContext`: fetches `/v1/auth/session` on mount (returns `{ userId, orgs }` only — see GAP-4); extracts current org from URL params; exposes `currentOrg`, `allOrgs`, `currentRole`, `isOwner`; sources `email` and `displayName` from WorkOS AuthKit `useAuth()`, not from the session API
- [x] **F-106** — Create `src/api/me.ts`: `useMe()` query, `useDeleteAccount()` mutation
- [x] **F-107** — Create `src/api/auth.ts`: `useSession()` query, `useSignOut()` mutation (calls `DELETE /v1/auth/session` then clears WorkOS AuthKit state)
- [x] **F-108** — Wire React Router: define all routes from §5; wrap all non-auth routes in `RequireAuth`; add `OrgContext` provider on org-scoped routes
- [x] **F-109** — Create `RequireOwner` wrapper component: reads `currentRole` from `OrgContext`; renders a "You need owner role" message if not `org:owner`; wrap owner-gated settings routes (members, IDP, org edit) (see GAP-7)

---

### Group 2 — Layout and navigation (depends on: Group 1)

- [x] **F-200** — Create `AppShell` component: sidebar (desktop) + top nav; sidebar items: Jobs, Settings (members, IDP, org), Profile link, Sign out
- [x] **F-201** — Create `OrgSwitcher` component in top nav: shows current org name + role badge; dropdown to switch org or create new
- [x] **F-202** — Create `OrgPickerPage` (`/orgs`): list user's orgs with role badges; **handle empty-orgs state explicitly** — show "No organization access" guidance to contact a platform admin or org admin for membership mapping.
- [ ] **F-203** — Remove self-service `CreateOrgPage` from primary user navigation (platform-admin-only org creation via admin plane).
- [x] **F-204** — Create `NotFoundPage`, `UnauthorizedPage`, `ErrorPage`
- [x] **F-205** — Create `src/api/orgs.ts`: `useOrgs()`, `useOrg(orgId)`, `useCreateOrg()`, `useUpdateOrg()`, `useDeleteOrg()`

---

### Group 3 — Dashboard and jobs list (depends on: Group 2)

- [x] **F-300** — Create `src/api/jobs.ts`: `useJobs(orgId, filters)`, `useJob(orgId, jobId)`, `useDeleteJob()`, polling logic in `useJob` based on status
- [x] **F-301** — Create `JobStatusBadge` component: maps all 10 statuses to color + label
- [x] **F-302** — Create `JobCard` component: title, status badge, artifact type chip (or "Manual" chip when `isManual == true` — see GAP-8), created date, "Continue" or "View" CTA based on status
- [x] **F-303** — Create `DashboardPage` (`/orgs/:orgId/jobs`): job list with status filter tabs, pagination, "New analysis" button, empty state; polling active jobs every 10s; auto-navigate on status change to review/analysis
- [x] **F-304** — Create delete job flow: confirm dialog with job title; call `useDeleteJob()`; show toast; remove from list

---

### Group 4 — Job submission (depends on: Group 3)

- [x] **F-400** — Create `SubmitJobPage` (`/orgs/:orgId/jobs/new`): two-option picker (upload / manual)
- [x] **F-401** — Create `UploadDropzone` component: `react-dropzone` with extension allowlist `['.png','.jpg','.jpeg','.gif','.webp','.puml','.txt','.md','.mmd','.drawio','.xml']`; client-side MIME + size check; preview filename; error display
- [x] **F-402** — Create `UploadJobPage` (`/orgs/:orgId/jobs/new/upload`): `UploadDropzone` + title field + submit; `multipart/form-data` POST to `POST /v1/orgs/:orgId/jobs`; progress bar during upload; handle 413/415/429 per §6.4; navigate to `/jobs/:jobId` on 202
- [x] **F-403** — Create `ManualJobPage` (`/orgs/:orgId/jobs/new/manual`): title + system purpose form; `POST /v1/orgs/:orgId/jobs/manual`; navigate to `/jobs/:jobId/review` on 201

---

### Group 5 — Job progress page (depends on: Group 4)

- [x] **F-500** — Create `JobDetailPage` (`/orgs/:orgId/jobs/:jobId`): pipeline stepper, CTA links, polling, auto-navigate on status transition, error display with `errorCode`; show "Manual job (no artifact)" in artifact type row when `isManual == true` (see GAP-8)

---

### Group 6 — Architecture canvas (depends on: Group 5)

This is the largest and most complex group.

- [x] **F-600** — Install `reactflow` (React Flow v12), `@dagrejs/dagre` (auto-layout)
- [x] **F-601** — Create `src/api/architecture.ts`: `useArchitecture(orgId, jobId)`, `useAddElement()`, `usePatchElement()`, `useDeleteElement()`, `useConfirmArchitecture()`, `useCorrectElement()` (calls `POST /elements/:elementId`), `useReanalyzeJob()` (calls `POST /architecture/reanalyze`)
- [x] **F-602** — Create element type constants and helpers: `ELEMENT_TYPE_CONFIG` mapping each `ElementType` to `{ label, color, icon, shape }` used across canvas and forms
- [x] **F-603** — Create `ElementNode` React Flow custom node: renders element with name, type badge, source badge (extracted/user-added), confidence dot; supports selected state
- [x] **F-604** — Create canvas layout helper: convert `ArchitectureElement[]` to React Flow `nodes` and `edges`; DataFlow elements become edges between their `from` and `to` elements (resolved by name); `TrustBoundary` elements become React Flow groups; auto-layout with dagre
- [x] **F-605** — Create `ArchCanvas` component: React Flow canvas with custom nodes; auto-layout on load; pan/zoom; node selection → fires `onElementSelect` callback; mini-map; controls (zoom-in, zoom-out, fit-view)
- [x] **F-606** — Create `ElementListPanel` (left panel): grouped element list (by type); search/filter; click → selects on canvas; "Add element" button at bottom
- [x] **F-607** — Create `AddElementModal`: element type selector with icons, name input (required), description textarea, properties form (known keys as labeled inputs; add/remove arbitrary key-value pairs); Zod validation; submit → `useAddElement()`
- [x] **F-608** — Create `ElementDetailPanel` (right panel): read-only mode showing all fields; edit mode with form for name, description, properties (same field structure as AddElementModal); save → `usePatchElement()`; delete button (with confirm) → `useDeleteElement()`; **Corrections section** (extracted elements): list existing corrections with type badge, field name, old/new value, timestamp; "Add correction" button opens `AddCorrectionModal` → `useCorrectElement()` with type selector (Update/MarkIncorrect/MarkAssumed/MarkConfirmed/AddNote), conditional fields (fieldName for Update, note for AddNote)
- [x] **F-609** — Create `ArchitectureMetaPanel`: collapsible; shows system purpose, assumptions checklist, gaps list, clarification questions with priority badges
- [x] **F-610** — Create `ReviewPage` (`/orgs/:orgId/jobs/:jobId/review`): compose all panels; top bar with job title, status chip, confirm button; confirm dialog with optional "Confirmation note" textarea (see GAP-9) — textarea value sent as `note` field in `ConfirmArchitectureRequest` body; handle non-`AWAITING_REVIEW` jobs (read-only with status note); disable confirm if 0 elements
- [x] **F-611** — Create accessibility fallback: below canvas, include `ElementListPanel` as a screen-reader-accessible `<table>` with all elements and their properties

---

### Group 7 — Threats and analysis (depends on: Group 6)

- [x] **F-700** — Create `src/api/threats.ts`: `useThreats(orgId, jobId, filters)`, `useThreat(orgId, jobId, threatId)` (calls `GET .../threats/:threatId`), `useAddThreat()`, `useUpdateThreatStatus()` (calls `PATCH .../threats/:threatId/status` — note `/status` suffix), `useAddThreatNote()`, `useAnalysis(orgId, jobId)` (calls `GET .../analysis` for in-page rendering), `useExportAnalysis(orgId, jobId)` (calls `GET .../export` for file download)
- [x] **F-701** — Create `ThreatStatusBadge` component: open (gray) / accepted (blue) / mitigated (green) / rejected (red)
- [x] **F-702** — Create `FindingTypeBadge`: confirmed (green) / conditional (amber) / user-added (purple)
- [x] **F-703** — Create `ThreatCard` component: identifier, title, method category, confidence, finding type, status badges, affected element names, description excerpt; click → opens detail
- [x] **F-704** — Create `ThreatDetailPanel`: full threat display; all fields; mitigations list with priority badges; framework mappings; notes thread; status update form; add note form
- [x] **F-705** — Create `ThreatFilterBar`: finding type checkboxes, status checkboxes, confidence checkboxes, method category dropdown; filters stored in URL search params
- [x] **F-706** — Create `AddThreatModal`: form with title (required), methodCategory (required), affectedElementIds (multi-select from element list), description, attackScenario, preconditions, impactedAssets, securityImpact, privacyImpact; Zod validation; submit → `useAddThreat()`
- [x] **F-707** — Create `AnalysisCanvas`: React Flow canvas (read-only variant of ArchCanvas); threat count badges overlaid on nodes; click node → filter threat list to that element; color-code node border by highest-severity threat — *implemented in `ArchCanvas` via `threatCountByElement` prop and `readOnly` mode*
- [x] **F-708** — Create `RecommendationsPanel`: list from analysis blob `secureDesignRecommendations`; title, description, principle chips, affected element list
- [x] **F-709** — Create `RemediationPanel`: prioritized list from analysis blob `prioritizedRemediationList`; grouped by priority; threat identifier links back to threat in Threats tab
- [x] **F-710** — Create `ExportPanel`: "Download JSON" button calls `GET /orgs/:orgId/jobs/:jobId/export` (streaming file download, `Content-Disposition: attachment`, returns `threat-model-{jobId}.json`) — see GAP-3; "Download Markdown" renders analysis data (from `useAnalysis()` cache) as Markdown client-side; the in-page analysis content is loaded separately via `GET .../analysis` (parsed JSON)
- [x] **F-711** — Create `AnalysisPage` (`/orgs/:orgId/jobs/:jobId/analysis`): summary header (system summary, classification chips, method cards, model routing info, threat counts); tabbed layout composing panels from F-707 to F-710; partial-analysis banner; review questions panel; "Re-analyze" button in top bar → calls `useReanalyzeJob()` with confirm dialog; on success navigate to `/jobs/:jobId/review`

---

### Group 8 — Settings (depends on: Group 2)

- [x] **F-800** — Create `src/api/members.ts`: `useMembers(orgId)`, `useInviteMember()`, `useUpdateMemberRole()`, `useRemoveMember()`
- [x] **F-801** — Create `src/api/idp.ts`: `useIdpConfig(orgId)`, `useUpsertIdpConfig()`, `useDeleteIdpConfig()`
- [x] **F-802** — Create `OrgSettingsPage` (`/orgs/:orgId/settings`): org name form (owner only); danger zone with delete-org confirm dialog (type org name to confirm)
- [x] **F-803** — Create `MembersPage` (`/orgs/:orgId/settings/members`): member list with avatars; invite form (owner only); role change dropdown; remove button with confirm; last-owner guard (show error from 409 LAST_OWNER code)
- [x] **F-804** — Create `IdpConfigPage` (`/orgs/:orgId/settings/idp`): owner-only; current config display; upsert form; delete config
- [x] **F-805** — Create `ProfilePage` (`/me`): user ID display; account created date; delete account with double-confirm dialog

---

### Group 9 — Polish and production-readiness (depends on: Groups 3–8)

- [x] **F-900** — Add `ErrorBoundary` wrapping router; friendly fallback UI with correlation ID from error response if available
- [x] **F-901** — Add global toast provider (sonner); wire all mutation success/error toasts
- [x] **F-902** — Add loading skeletons for all data-fetching pages (replace spinners with content-shaped skeletons)
- [x] **F-903** — Add `<title>` management: page title reflects current context (job title, org name, section)
- [x] **F-904** — Add keyboard navigation to canvas: Tab cycles through elements; Enter opens detail panel; Delete removes selected user-added element (with confirm)
- [x] **F-905** — Add `manifest.json` and favicons for PWA-lite installability (no service worker at MVP)
- [x] **F-906** — Performance: verify bundle analysis with `rollup-plugin-visualizer`; ensure no chunk exceeds 250 kB uncompressed; code-split all pages
- [x] **F-907** — Accessibility audit: run `axe-core` via `@axe-core/react` in dev mode; fix all critical and serious issues before GA

---

### Group 10 — Tests (depends on: Groups 1–8)

Unit/component tests:
- [x] **F-T01** — `UploadDropzone`: accepts allowed extensions; rejects disallowed extensions; rejects files > 10 MB; shows correct error messages
- [x] **F-T02** — `JobStatusBadge`: renders correct label and color class for all 10 statuses
- [x] **F-T03** — `ThreatCard`: renders all fields; clicking fires select callback
- [x] **F-T04** — `AddElementModal`: validates required fields; rejects empty name; submits correct payload; clears on success
- [x] **F-T05** — `OrgContext`: returns correct `isOwner` for `owner` and `member` roles
- [x] **F-T06** — `client.ts` interceptor: attaches Bearer token; retries once on 401; redirects to login on second 401
- [x] **F-T07** — `AuthCallbackPage`: validates `return_to` rejects external URLs (e.g. `https://evil.com`, `//evil.com`, `javascript:`)
- [x] **F-T08** — `ThreatFilterBar`: updating filters changes URL params; correct params passed to query
- [x] **F-T09** — `ElementDetailPanel`: save button calls `usePatchElement` with correct payload; delete button shows confirm dialog
- [x] **F-T10** — `ExportPanel`: "Download JSON" calls correct API endpoint; does not include auth token in filename

E2E tests (Playwright — requires running API):
- [x] **F-E01** — Full upload flow: upload PNG file → job created → wait for AWAITING_REVIEW → review page shown with elements — *test.fixme; full flow needs API + auth helpers*
- [x] **F-E02** — Full manual flow: create manual job → add elements → confirm → job transitions to analysis — *test.fixme; full flow needs API + auth helpers*
- [x] **F-E03** — Threat status update: open job in COMPLETE status → change a threat's status → verify change persists on reload — *test.fixme; needs API + seeded data*
- [x] **F-E04** — Cross-org isolation: user in org A cannot navigate to org B's job URL (redirected or shown 404) — *stub created (test.skip)*
- [x] **F-E05** — Auth: unauthenticated navigation to protected route redirects to login with `return_to`; after login, lands on correct page
- [x] **F-E06** — Member invite: owner invites email → success toast shown; non-owner cannot see invite form — *test.fixme; needs owner session*
- [x] **F-E07** — Export: analysis page export tab → "Download JSON" calls `/export` (not `/analysis`) → file downloaded with `Content-Disposition: attachment` filename `threat-model-{jobId}.json` — *stub created (test.skip)*

---

## 13. Dependency Versions (initial, to be pinned exactly at scaffold time)

| Package | Minimum version |
|---|---|
| react | 19.x |
| react-dom | 19.x |
| react-router-dom | 7.x |
| @tanstack/react-query | 5.x |
| react-hook-form | 7.x |
| zod | 3.x |
| axios | 1.x |
| tailwindcss | 4.x |
| @radix-ui/* | latest stable |
| reactflow | 12.x |
| @dagrejs/dagre | 1.x |
| react-dropzone | 14.x |
| @workos-inc/authkit-react | latest stable |
| sonner | 1.x |
| date-fns | 4.x |
| lucide-react | 0.x (latest) |
| openapi-typescript | 7.x |
| vitest | 2.x |
| @testing-library/react | 16.x |
| playwright | 1.x |
| typescript | 5.x |
| vite | 6.x |

---

## 14. Backend API Gaps Affecting the Frontend

These are discrepancies between `openapi.yaml`, the actual controller implementations, and the frontend spec. They must be resolved — either by implementing the missing backend endpoint or by adjusting the frontend spec — before the affected frontend items are built.

### GAP-1 — ~~`GET /threats/:threatId` and element-level `PATCH /threats/:threatId` path mismatch~~ ✅ RESOLVED

`GET /threats/:threatId` added to `ThreatsController`. `PATCH` path fixed to `/status` in both controller and openapi.yaml. `useThreat()` hook enabled in F-700.

### GAP-2 — ~~`POST /elements/:elementId` (CorrectElementRequest) not implemented~~ ✅ RESOLVED

`POST /orgs/:orgId/jobs/:jobId/elements/:elementId` implemented in `ArchitecturesController`. Path also corrected (no `/architecture/` prefix). `Corrections` array is now populated in all element responses. Corrections section enabled in F-608.

### GAP-3 — ~~`/export` endpoint undocumented in openapi.yaml~~ ✅ RESOLVED

`GET /orgs/:orgId/jobs/:jobId/export` added to openapi.yaml. F-710 correctly calls `/export` for file download and `/analysis` for in-page rendering.

### GAP-4 — ~~Session endpoint returns `{ userId, orgs }` — no email or displayName~~ ✅ RESOLVED

`SessionResponse` in openapi.yaml updated to match actual backend shape (removed `email`/`displayName`). F-105 `OrgContext` sources display data from WorkOS AuthKit `useAuth()` hook, not the session API.

### GAP-5 — ~~No re-analysis mechanism~~ ✅ RESOLVED

`POST /orgs/:orgId/jobs/:jobId/architecture/reanalyze` implemented. State machine allows `Complete/Partial → AwaitingReview`. `Architecture.ResetForReanalysis()` clears confirmation and bumps version. System-generated threats deleted on reanalyze. "Re-analyze" button added to F-711 `AnalysisPage`. OD-F7 is no longer deferred.

### GAP-6 — First-time user / no-orgs state (authority model update)

When a user authenticates and `GET /auth/session` returns `orgs: []` (or org-scoped calls are denied due to no membership mapping), the user is authenticated but not authorised for any org context.

**Resolution (frontend only):**
- F-202 must handle the empty-orgs case explicitly: show a no-access state and remove self-service org creation CTA from user routes.
- The message should direct the user to contact a platform admin for org provisioning and membership mapping.

### GAP-7 — `RequireOwner` guard not in backlog

Several routes require `org:owner` role (members management, IDP config, org settings edit, delete org). The spec mentions "owner only" per-page but no reusable guard component is specified.

**Resolution (frontend only):**
- Add **F-109** to Group 1: `RequireOwner` wrapper component — reads `currentRole` from `OrgContext`; renders 403 / insufficient-permission view if not owner; used on owner-gated settings routes.

### GAP-8 — `isManual` field not reflected in job list or detail UI

`JobDetailDto` includes `isManual: true` for manual jobs. The frontend spec does not show this anywhere.

**Resolution (minor, frontend only):**
- F-302 `JobCard`: show "Manual" chip where artifact type chip would normally appear when `isManual == true`.
- F-500 `JobDetailPage`: show "Manual job (no artifact)" in the artifact type row.

### GAP-9 — Confirm architecture note field not surfaced

`ConfirmArchitectureRequest` accepts an optional `Note` string. The confirm dialog in F-610 doesn't expose this.

**Resolution (minor, frontend only):**
- F-610: add optional "Confirmation note" textarea to the confirm dialog. Passed in request body if non-empty.

### GAP-TH1 — `AddThreatModal` never submits `affectedElementIds` ✅ RESOLVED

**Spec ref:** `01-product.md §19`, `03-data-model.md §9`  
**Severity:** MUST

`AddThreatModal` accepts an `elements` prop (wired from `AnalysisPage`) but renders no element selector UI. The submitted payload always contains `affectedElementIds: []`, violating the data-model invariant "A threat MUST reference at least one `architecture_element`."

This applies equally to threats added for uploaded architectures and manually drawn architectures — the API does not discriminate, but it also does not currently enforce the invariant (see GAP-TH2).

**Required changes (frontend):**
- Add a multi-select element picker to `AddThreatModal` using the `elements` prop that is already passed in.
- Validate at submit: at least one element must be selected.
- Emit the selected IDs as `affectedElementIds` in the POST body.

### GAP-TH2 — Domain and API do not enforce non-empty `affectedElementIds` ✅ RESOLVED

**Spec ref:** `03-data-model.md §9`  
**Severity:** MUST

`Threat.CreateUserAdded()` accepts `Guid[] affectedElementIds` with no minimum-length check. `ThreatsController.AddThreat` passes `request.AffectedElementIds ?? []` directly. The domain invariant is not enforced at either layer.

**Required changes (backend):**
- `Threat.CreateUserAdded()`: throw `DomainException` (or equivalent) if `affectedElementIds` is empty.
- `ThreatsController.AddThreat`: return HTTP 422 with error code `ELEMENT_REQUIRED` if `affectedElementIds` is null or empty.
- Add integration test: `POST /threats` with empty `affectedElementIds` → 422.

### GAP-TH3 — Canvas element click does not filter threat list ✅ RESOLVED

**Spec ref:** `01-product.md §19` — "click a diagram element and see the threats mapped to that element"  
**Severity:** MUST

`AnalysisPage` calls `setSelectedThreat(null)` on canvas element select but does not write an `elementId` URL param. The threat list is not filtered by element.

**Required changes:**
- `AnalysisPage.onElementSelect`: write selected element id to URL search param `elementId`; clear on deselect.
- `ThreatFilterBar`: add element filter chip (hidden when no element selected, visible when active, "×" clears it).
- `useThreats` (or equivalent query): pass `elementId` to API list endpoint if present.
- API: `GET /threats` needs to accept `?elementId=` query param and filter `threat_elements` join accordingly.

### GAP-TH4 — DataFlow edges have no threat overlay ✅ RESOLVED

**Spec ref:** `01-product.md §19` — "click a data flow and see threats mapped to that flow"  
**Severity:** SHOULD

DataFlow edges in `ArchCanvas` render as ReactFlow edges but carry no threat-count badge and `onEdgeClick` is not wired.

**Required changes:**
- Compute per-edge threat counts from the `threats` list (match edge id against `affectedElementIds`).
- Render a count badge on each edge (ReactFlow custom edge type).
- Wire `onEdgeClick` → set `elementId` filter param (same mechanism as GAP-TH3).

### GAP-TH5 — `ElementDetailPanel` shows no related threats or mitigations ✅ RESOLVED

**Spec ref:** `01-product.md §19` — SHOULD per-element views  
**Severity:** SHOULD

`ElementDetailPanel` (used on `ReviewPage`) shows metadata and corrections only. `AnalysisPage` has no equivalent per-element panel at all.

**Required changes:**
- Extend `ElementDetailPanel` or create a separate `AnalysisElementPanel` for `AnalysisPage`.
- Panel shows: threats referencing the element (list with status chips), mitigations summary, related framework mappings.
- Panel is triggered by canvas element click (via `elementId` state from GAP-TH3).

### GAP-TH7 — Pre-analysis threat addition blocked by API status gate ✅ RESOLVED

**Spec ref:** `01-product.md §19` — "the user can add their own threats or concerns" in the pre-analysis correction workflow  
**Severity:** MUST

The `POST /threats` endpoint requires job status `Complete or Partial`. During `AwaitingReview`, the user cannot add threats or concerns via the API. The `ReviewPage` also has no add-threat UI.

**Required changes (backend):**
- Expand the status gate on `AddThreat` to also permit `AwaitingReview`, **or** add a separate `POST /concerns` endpoint for pre-analysis notes (with narrower fields and no `affectedElementIds` requirement).
- Add integration test: add threat/concern during `AwaitingReview` → 201.

**Required changes (frontend):**
- Add an "Add concern" or "Flag for analysis" button on `ReviewPage` (displayed when job is `AwaitingReview`).
- These pre-analysis concerns should persist into the analysis as user-added threats with status `UserAdded`.

---

## 15. Open Decisions

| ID | Decision | Impact |
|---|---|---|
| OD-F1 | SSE vs polling: spec uses polling (OD-3 resolution); post-MVP may switch to SSE when backend adds support | Job progress UX |
| OD-F2 | Canvas layout persistence: cosmetic layout (node positions) is not persisted to backend in MVP; user must re-layout on each visit | Review page UX |
| OD-F3 | DataFlow elements as canvas nodes vs edges: currently `DataFlow` elements are rendered as directed edges using `from`/`to` properties; elements without both properties fall back to a node | Canvas correctness |
| OD-F4 | CSV export: deferred to post-MVP (requires server-side formula sanitization per CLAUDE.md §7.8); only JSON and Markdown available at launch | Export features |
| OD-F5 | **GAP-TH6** — Diagram state comparison (spec §19): deferred to post-MVP — shows original extracted vs corrected vs current overlay on the canvas | Architecture review UX |
| OD-F6 | Mobile support for canvas: deferred; mobile shows list-only view; canvas is desktop-only | Mobile UX |
| OD-F7 | ~~Re-analysis after completion: deferred to post-MVP~~ **RESOLVED** — backend endpoint `POST /architecture/reanalyze` implemented, state machine updated, "Re-analyze" button in F-711 | Analysis workflow |
