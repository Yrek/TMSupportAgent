using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThreatModelingAgent.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Enables PostgreSQL Row-Level Security on all tenant-scoped tables.
    ///
    /// Each policy compares the row's org_id against the 'app.current_org_id' session
    /// variable set by RlsSessionInterceptor via SET LOCAL before every query.
    ///
    /// FORCE ROW LEVEL SECURITY ensures table owners cannot bypass the policies —
    /// all application roles are subject to the same isolation rules (CLAUDE.md §4.5).
    ///
    /// current_setting('app.current_org_id', true) — the second arg (missing_ok=true)
    /// returns NULL rather than throwing when the variable is unset. A NULL org_id
    /// matches no rows, so failing to set the session variable fails closed (CLAUDE.md §4.3).
    /// </summary>
    public partial class AddRowLevelSecurity : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── jobs ──────────────────────────────────────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE jobs ENABLE ROW LEVEL SECURITY;
                ALTER TABLE jobs FORCE ROW LEVEL SECURITY;

                CREATE POLICY jobs_tenant_isolation ON jobs
                  USING  (org_id::text = current_setting('app.current_org_id', true))
                  WITH CHECK (org_id::text = current_setting('app.current_org_id', true));
            ");

            // ── org_memberships ───────────────────────────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE org_memberships ENABLE ROW LEVEL SECURITY;
                ALTER TABLE org_memberships FORCE ROW LEVEL SECURITY;

                CREATE POLICY org_memberships_tenant_isolation ON org_memberships
                  USING  (org_id::text = current_setting('app.current_org_id', true))
                  WITH CHECK (org_id::text = current_setting('app.current_org_id', true));
            ");

            // ── org_idp_configs ───────────────────────────────────────────────
            migrationBuilder.Sql(@"
                ALTER TABLE org_idp_configs ENABLE ROW LEVEL SECURITY;
                ALTER TABLE org_idp_configs FORCE ROW LEVEL SECURITY;

                CREATE POLICY org_idp_configs_tenant_isolation ON org_idp_configs
                  USING  (org_id::text = current_setting('app.current_org_id', true))
                  WITH CHECK (org_id::text = current_setting('app.current_org_id', true));
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS jobs_tenant_isolation ON jobs;
                ALTER TABLE jobs DISABLE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS org_memberships_tenant_isolation ON org_memberships;
                ALTER TABLE org_memberships DISABLE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS org_idp_configs_tenant_isolation ON org_idp_configs;
                ALTER TABLE org_idp_configs DISABLE ROW LEVEL SECURITY;
            ");
        }
    }
}
