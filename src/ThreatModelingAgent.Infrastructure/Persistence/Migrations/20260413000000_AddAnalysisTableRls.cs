using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThreatModelingAgent.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Enables PostgreSQL Row-Level Security on the 8 tenant-scoped analysis tables
    /// created by AddOrgSuspension (architectures, architecture_elements,
    /// architecture_corrections, threats, threat_notes, mitigations,
    /// framework_mappings, rejected_candidates).
    ///
    /// Each policy compares the row's org_id against the 'app.current_org_id' session
    /// variable set by RlsSessionInterceptor before every query (fail-closed: missing
    /// variable returns NULL which matches no rows — CLAUDE.md §4.3).
    ///
    /// FORCE ROW LEVEL SECURITY ensures table owners (the app role) cannot bypass the
    /// policies (CLAUDE.md §4.5).
    ///
    /// DROP POLICY IF EXISTS before each CREATE POLICY makes this migration idempotent
    /// so re-running MigrateAsync() on the same database never fails.
    /// </summary>
    public partial class AddAnalysisTableRls : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE architectures ENABLE ROW LEVEL SECURITY;
                ALTER TABLE architectures FORCE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS architectures_tenant_isolation ON architectures;
                CREATE POLICY architectures_tenant_isolation ON architectures
                  USING  (org_id::text = current_setting('app.current_org_id', true))
                  WITH CHECK (org_id::text = current_setting('app.current_org_id', true));

                ALTER TABLE architecture_elements ENABLE ROW LEVEL SECURITY;
                ALTER TABLE architecture_elements FORCE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS arch_elements_tenant_isolation ON architecture_elements;
                CREATE POLICY arch_elements_tenant_isolation ON architecture_elements
                  USING  (org_id::text = current_setting('app.current_org_id', true))
                  WITH CHECK (org_id::text = current_setting('app.current_org_id', true));

                ALTER TABLE architecture_corrections ENABLE ROW LEVEL SECURITY;
                ALTER TABLE architecture_corrections FORCE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS arch_corrections_tenant_isolation ON architecture_corrections;
                CREATE POLICY arch_corrections_tenant_isolation ON architecture_corrections
                  USING  (org_id::text = current_setting('app.current_org_id', true))
                  WITH CHECK (org_id::text = current_setting('app.current_org_id', true));

                ALTER TABLE threats ENABLE ROW LEVEL SECURITY;
                ALTER TABLE threats FORCE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS threats_tenant_isolation ON threats;
                CREATE POLICY threats_tenant_isolation ON threats
                  USING  (org_id::text = current_setting('app.current_org_id', true))
                  WITH CHECK (org_id::text = current_setting('app.current_org_id', true));

                ALTER TABLE threat_notes ENABLE ROW LEVEL SECURITY;
                ALTER TABLE threat_notes FORCE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS threat_notes_tenant_isolation ON threat_notes;
                CREATE POLICY threat_notes_tenant_isolation ON threat_notes
                  USING  (org_id::text = current_setting('app.current_org_id', true))
                  WITH CHECK (org_id::text = current_setting('app.current_org_id', true));

                ALTER TABLE mitigations ENABLE ROW LEVEL SECURITY;
                ALTER TABLE mitigations FORCE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS mitigations_tenant_isolation ON mitigations;
                CREATE POLICY mitigations_tenant_isolation ON mitigations
                  USING  (org_id::text = current_setting('app.current_org_id', true))
                  WITH CHECK (org_id::text = current_setting('app.current_org_id', true));

                ALTER TABLE framework_mappings ENABLE ROW LEVEL SECURITY;
                ALTER TABLE framework_mappings FORCE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS framework_mappings_tenant_isolation ON framework_mappings;
                CREATE POLICY framework_mappings_tenant_isolation ON framework_mappings
                  USING  (org_id::text = current_setting('app.current_org_id', true))
                  WITH CHECK (org_id::text = current_setting('app.current_org_id', true));

                ALTER TABLE rejected_candidates ENABLE ROW LEVEL SECURITY;
                ALTER TABLE rejected_candidates FORCE ROW LEVEL SECURITY;
                DROP POLICY IF EXISTS rejected_candidates_tenant_isolation ON rejected_candidates;
                CREATE POLICY rejected_candidates_tenant_isolation ON rejected_candidates
                  USING  (org_id::text = current_setting('app.current_org_id', true))
                  WITH CHECK (org_id::text = current_setting('app.current_org_id', true));
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS architectures_tenant_isolation ON architectures;
                ALTER TABLE architectures DISABLE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS arch_elements_tenant_isolation ON architecture_elements;
                ALTER TABLE architecture_elements DISABLE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS arch_corrections_tenant_isolation ON architecture_corrections;
                ALTER TABLE architecture_corrections DISABLE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS threats_tenant_isolation ON threats;
                ALTER TABLE threats DISABLE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS threat_notes_tenant_isolation ON threat_notes;
                ALTER TABLE threat_notes DISABLE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS mitigations_tenant_isolation ON mitigations;
                ALTER TABLE mitigations DISABLE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS framework_mappings_tenant_isolation ON framework_mappings;
                ALTER TABLE framework_mappings DISABLE ROW LEVEL SECURITY;

                DROP POLICY IF EXISTS rejected_candidates_tenant_isolation ON rejected_candidates;
                ALTER TABLE rejected_candidates DISABLE ROW LEVEL SECURITY;
            ");
        }
    }
}
