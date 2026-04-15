import { createBrowserRouter, Navigate } from "react-router-dom";
import { RequireAuth } from "@/components/common/RequireAuth";
import { RequireOwner } from "@/components/common/RequireOwner";
import { RequirePlatformAdmin } from "@/components/common/RequirePlatformAdmin";
import { OrgProvider } from "@/components/common/OrgProvider";
import { RouteErrorBoundary } from "@/components/common/RouteErrorBoundary";
import { LoginPage } from "@/pages/auth/LoginPage";
import { AuthCallbackPage } from "@/pages/auth/AuthCallbackPage";

// Lazy-loaded pages — code-split per route
import { lazy, Suspense } from "react";
import { Spinner } from "@/components/common/Spinner";

function withSuspense(Component: React.ComponentType) {
  return (
    <Suspense
      fallback={
        <div className="flex min-h-screen items-center justify-center">
          <Spinner />
        </div>
      }
    >
      <Component />
    </Suspense>
  );
}

const OrgPickerPage = lazy(() =>
  import("@/pages/dashboard/OrgPickerPage").then((m) => ({ default: m.OrgPickerPage })),
);
const DashboardPage = lazy(() =>
  import("@/pages/dashboard/DashboardPage").then((m) => ({ default: m.DashboardPage })),
);
const SubmitJobPage = lazy(() =>
  import("@/pages/jobs/SubmitJobPage").then((m) => ({ default: m.SubmitJobPage })),
);
const UploadJobPage = lazy(() =>
  import("@/pages/jobs/UploadJobPage").then((m) => ({ default: m.UploadJobPage })),
);
const ManualJobPage = lazy(() =>
  import("@/pages/jobs/ManualJobPage").then((m) => ({ default: m.ManualJobPage })),
);
const JobDetailPage = lazy(() =>
  import("@/pages/jobs/JobDetailPage").then((m) => ({ default: m.JobDetailPage })),
);
const ReviewPage = lazy(() =>
  import("@/pages/jobs/ReviewPage").then((m) => ({ default: m.ReviewPage })),
);
const AnalysisPage = lazy(() =>
  import("@/pages/jobs/AnalysisPage").then((m) => ({ default: m.AnalysisPage })),
);
const OrgSettingsPage = lazy(() =>
  import("@/pages/settings/OrgSettingsPage").then((m) => ({ default: m.OrgSettingsPage })),
);
const MembersPage = lazy(() =>
  import("@/pages/settings/MembersPage").then((m) => ({ default: m.MembersPage })),
);
const IdpConfigPage = lazy(() =>
  import("@/pages/settings/IdpConfigPage").then((m) => ({ default: m.IdpConfigPage })),
);
const OrgAuditPage = lazy(() =>
  import("@/pages/settings/OrgAuditPage").then((m) => ({ default: m.OrgAuditPage })),
);
const ProfilePage = lazy(() =>
  import("@/pages/settings/ProfilePage").then((m) => ({ default: m.ProfilePage })),
);
const AdminDashboardPage = lazy(() =>
  import("@/pages/admin/AdminDashboardPage").then((m) => ({ default: m.AdminDashboardPage })),
);
const AdminOrgsPage = lazy(() =>
  import("@/pages/admin/AdminOrgsPage").then((m) => ({ default: m.AdminOrgsPage })),
);
const AdminOrgDetailPage = lazy(() =>
  import("@/pages/admin/AdminOrgDetailPage").then((m) => ({ default: m.AdminOrgDetailPage })),
);
const NotFoundPage = lazy(() =>
  import("@/pages/errors/NotFoundPage").then((m) => ({ default: m.NotFoundPage })),
);
const UnauthorizedPage = lazy(() =>
  import("@/pages/errors/UnauthorizedPage").then((m) => ({ default: m.UnauthorizedPage })),
);
const ErrorPage = lazy(() =>
  import("@/pages/errors/ErrorPage").then((m) => ({ default: m.ErrorPage })),
);

function AuthRequired({ children }: { children: React.ReactNode }) {
  return <RequireAuth>{children}</RequireAuth>;
}

function OrgScoped({ children }: { children: React.ReactNode }) {
  return (
    <RequireAuth>
      <OrgProvider>{children}</OrgProvider>
    </RequireAuth>
  );
}

function OrgScopedOwner({ children }: { children: React.ReactNode }) {
  return (
    <RequireAuth>
      <OrgProvider>
        <RequireOwner>{children}</RequireOwner>
      </OrgProvider>
    </RequireAuth>
  );
}

export const router = createBrowserRouter([
  {
    // Root wrapper — errorElement catches all unhandled route errors
    errorElement: <RouteErrorBoundary />,
    children: [
  // Public auth routes
  { path: "/login", element: <LoginPage /> },
  { path: "/auth/callback", element: <AuthCallbackPage /> },

  // Root redirect
  {
    path: "/",
    element: (
      <AuthRequired>
        <Navigate to="/orgs" replace />
      </AuthRequired>
    ),
  },

  // Org picker (authenticated, no org scope)
  {
    path: "/orgs",
    element: <AuthRequired>{withSuspense(OrgPickerPage)}</AuthRequired>,
  },

  // Org-scoped routes
  {
    path: "/orgs/:orgId/jobs",
    element: <OrgScoped>{withSuspense(DashboardPage)}</OrgScoped>,
  },
  {
    path: "/orgs/:orgId/jobs/new",
    element: <OrgScoped>{withSuspense(SubmitJobPage)}</OrgScoped>,
  },
  {
    path: "/orgs/:orgId/jobs/new/upload",
    element: <OrgScoped>{withSuspense(UploadJobPage)}</OrgScoped>,
  },
  {
    path: "/orgs/:orgId/jobs/new/manual",
    element: <OrgScoped>{withSuspense(ManualJobPage)}</OrgScoped>,
  },
  {
    path: "/orgs/:orgId/jobs/:jobId",
    element: <OrgScoped>{withSuspense(JobDetailPage)}</OrgScoped>,
  },
  {
    path: "/orgs/:orgId/jobs/:jobId/review",
    element: <OrgScoped>{withSuspense(ReviewPage)}</OrgScoped>,
  },
  {
    path: "/orgs/:orgId/jobs/:jobId/analysis",
    element: <OrgScoped>{withSuspense(AnalysisPage)}</OrgScoped>,
  },

  // Settings — read access for all, write access gated per-page
  {
    path: "/orgs/:orgId/settings",
    element: <OrgScoped>{withSuspense(OrgSettingsPage)}</OrgScoped>,
  },
  {
    path: "/orgs/:orgId/settings/members",
    element: <OrgScopedOwner>{withSuspense(MembersPage)}</OrgScopedOwner>,
  },
  {
    path: "/orgs/:orgId/settings/idp",
    element: <OrgScopedOwner>{withSuspense(IdpConfigPage)}</OrgScopedOwner>,
  },
  {
    path: "/orgs/:orgId/settings/audit",
    element: <OrgScopedOwner>{withSuspense(OrgAuditPage)}</OrgScopedOwner>,
  },

  // Profile (no org scope)
  {
    path: "/me",
    element: <AuthRequired>{withSuspense(ProfilePage)}</AuthRequired>,
  },

  // Platform admin routes — require platform:admin JWT role
  {
    path: "/admin",
    element: (
      <RequireAuth>
        <RequirePlatformAdmin>{withSuspense(AdminDashboardPage)}</RequirePlatformAdmin>
      </RequireAuth>
    ),
  },
  {
    path: "/admin/orgs",
    element: (
      <RequireAuth>
        <RequirePlatformAdmin>{withSuspense(AdminOrgsPage)}</RequirePlatformAdmin>
      </RequireAuth>
    ),
  },
  {
    path: "/admin/orgs/:orgId",
    element: (
      <RequireAuth>
        <RequirePlatformAdmin>{withSuspense(AdminOrgDetailPage)}</RequirePlatformAdmin>
      </RequireAuth>
    ),
  },

  // Error pages
  { path: "/unauthorized", element: withSuspense(UnauthorizedPage) },
  { path: "/error", element: withSuspense(ErrorPage) },
  { path: "*", element: withSuspense(NotFoundPage) },
    ], // end root children
  },  // end root wrapper
]);
