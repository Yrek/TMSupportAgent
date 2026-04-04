using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThreatModelingAgent.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adds the 8 tables required by data-model spec §4.6–4.13:
    /// architectures, architecture_elements, architecture_corrections,
    /// threats, threat_notes, mitigations, framework_mappings, rejected_candidates.
    ///
    /// Also enables Row-Level Security on the 5 tenant-scoped tables
    /// added by this migration (jobs, org_memberships, org_idp_configs
    /// were already covered in AddRowLevelSecurity).
    /// </summary>
    public partial class AddAnalysisTables : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── architectures ──────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "architectures",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    classification = table.Column<string[]>(type: "text[]", nullable: false),
                    system_purpose = table.Column<string>(type: "text", nullable: true),
                    assumptions = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    gaps = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    clarification_questions = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "[]"),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    confirmed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_architectures", x => x.id);
                    table.ForeignKey("FK_architectures_jobs_job_id", x => x.job_id, "jobs", "id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex("IX_architectures_job_id", "architectures", "job_id", unique: true);
            migrationBuilder.CreateIndex("IX_architectures_org_id", "architectures", "org_id");

            // ── architecture_elements ──────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "architecture_elements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    architecture_id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    element_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    properties = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    extraction_confidence = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_architecture_elements", x => x.id);
                    table.ForeignKey("FK_architecture_elements_architectures_architecture_id",
                        x => x.architecture_id, "architectures", "id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex("IX_arch_elements_arch_id", "architecture_elements", "architecture_id");
            migrationBuilder.CreateIndex("IX_arch_elements_type", "architecture_elements",
                new[] { "architecture_id", "element_type" });

            // ── architecture_corrections ───────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "architecture_corrections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    element_id = table.Column<Guid>(type: "uuid", nullable: true),
                    architecture_id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    corrected_by = table.Column<Guid>(type: "uuid", nullable: false),
                    correction_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    field_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    original_value = table.Column<string>(type: "text", nullable: true),
                    corrected_value = table.Column<string>(type: "text", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_architecture_corrections", x => x.id);
                    table.ForeignKey("FK_arch_corrections_architectures_architecture_id",
                        x => x.architecture_id, "architectures", "id", onDelete: ReferentialAction.Cascade);
                    table.ForeignKey("FK_arch_corrections_elements_element_id",
                        x => x.element_id, "architecture_elements", "id", onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex("IX_arch_corrections_arch_id", "architecture_corrections", "architecture_id");

            // ── threats ───────────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "threats",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    identifier = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    method_category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    affected_element_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    attack_scenario = table.Column<string>(type: "text", nullable: false),
                    preconditions = table.Column<string>(type: "text", nullable: true),
                    impacted_assets = table.Column<string[]>(type: "text[]", nullable: false),
                    security_impact = table.Column<string>(type: "text", nullable: true),
                    privacy_impact = table.Column<string>(type: "text", nullable: true),
                    existing_controls = table.Column<string>(type: "text", nullable: true),
                    control_gaps = table.Column<string>(type: "text", nullable: true),
                    confidence = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    evidence_basis = table.Column<string[]>(type: "text[]", nullable: false),
                    evidence_strength = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    assumptions = table.Column<string>(type: "text", nullable: true),
                    finding_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_threats", x => x.id);
                    table.ForeignKey("FK_threats_jobs_job_id", x => x.job_id, "jobs", "id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex("IX_threats_job_identifier", "threats",
                new[] { "job_id", "identifier" }, unique: true);
            migrationBuilder.CreateIndex("IX_threats_job_id", "threats", "job_id");
            migrationBuilder.CreateIndex("IX_threats_job_finding_type", "threats",
                new[] { "job_id", "finding_type" });

            // ── threat_notes ──────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "threat_notes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    threat_id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_threat_notes", x => x.id);
                    table.ForeignKey("FK_threat_notes_threats_threat_id",
                        x => x.threat_id, "threats", "id", onDelete: ReferentialAction.Cascade);
                });

            // ── mitigations ───────────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "mitigations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    threat_id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    priority = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_mitigations", x => x.id);
                    table.ForeignKey("FK_mitigations_threats_threat_id",
                        x => x.threat_id, "threats", "id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex("IX_mitigations_threat_id", "mitigations", "threat_id");

            // ── framework_mappings ────────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "framework_mappings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    threat_id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    framework = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    mapping_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_framework_mappings", x => x.id);
                    table.ForeignKey("FK_framework_mappings_threats_threat_id",
                        x => x.threat_id, "threats", "id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex("IX_framework_mappings_threat_id", "framework_mappings", "threat_id");

            // ── rejected_candidates ───────────────────────────────────────────
            migrationBuilder.CreateTable(
                name: "rejected_candidates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    method_category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    rejection_reason = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    rejection_note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_rejected_candidates", x => x.id);
                    table.ForeignKey("FK_rejected_candidates_jobs_job_id",
                        x => x.job_id, "jobs", "id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex("IX_rejected_candidates_job_id", "rejected_candidates", "job_id");

            // ── Row-Level Security for new tenant-scoped tables ───────────────
            migrationBuilder.Sql(@"
                ALTER TABLE architectures ENABLE ROW LEVEL SECURITY;
                ALTER TABLE architectures FORCE ROW LEVEL SECURITY;
                CREATE POLICY architectures_tenant_isolation ON architectures
                  USING  (org_id::text = current_setting('app.current_org_id', true))
                  WITH CHECK (org_id::text = current_setting('app.current_org_id', true));

                ALTER TABLE architecture_elements ENABLE ROW LEVEL SECURITY;
                ALTER TABLE architecture_elements FORCE ROW LEVEL SECURITY;
                CREATE POLICY arch_elements_tenant_isolation ON architecture_elements
                  USING  (org_id::text = current_setting('app.current_org_id', true))
                  WITH CHECK (org_id::text = current_setting('app.current_org_id', true));

                ALTER TABLE architecture_corrections ENABLE ROW LEVEL SECURITY;
                ALTER TABLE architecture_corrections FORCE ROW LEVEL SECURITY;
                CREATE POLICY arch_corrections_tenant_isolation ON architecture_corrections
                  USING  (org_id::text = current_setting('app.current_org_id', true))
                  WITH CHECK (org_id::text = current_setting('app.current_org_id', true));

                ALTER TABLE threats ENABLE ROW LEVEL SECURITY;
                ALTER TABLE threats FORCE ROW LEVEL SECURITY;
                CREATE POLICY threats_tenant_isolation ON threats
                  USING  (org_id::text = current_setting('app.current_org_id', true))
                  WITH CHECK (org_id::text = current_setting('app.current_org_id', true));

                ALTER TABLE threat_notes ENABLE ROW LEVEL SECURITY;
                ALTER TABLE threat_notes FORCE ROW LEVEL SECURITY;
                CREATE POLICY threat_notes_tenant_isolation ON threat_notes
                  USING  (org_id::text = current_setting('app.current_org_id', true))
                  WITH CHECK (org_id::text = current_setting('app.current_org_id', true));

                ALTER TABLE mitigations ENABLE ROW LEVEL SECURITY;
                ALTER TABLE mitigations FORCE ROW LEVEL SECURITY;
                CREATE POLICY mitigations_tenant_isolation ON mitigations
                  USING  (org_id::text = current_setting('app.current_org_id', true))
                  WITH CHECK (org_id::text = current_setting('app.current_org_id', true));

                ALTER TABLE framework_mappings ENABLE ROW LEVEL SECURITY;
                ALTER TABLE framework_mappings FORCE ROW LEVEL SECURITY;
                CREATE POLICY framework_mappings_tenant_isolation ON framework_mappings
                  USING  (org_id::text = current_setting('app.current_org_id', true))
                  WITH CHECK (org_id::text = current_setting('app.current_org_id', true));

                ALTER TABLE rejected_candidates ENABLE ROW LEVEL SECURITY;
                ALTER TABLE rejected_candidates FORCE ROW LEVEL SECURITY;
                CREATE POLICY rejected_candidates_tenant_isolation ON rejected_candidates
                  USING  (org_id::text = current_setting('app.current_org_id', true))
                  WITH CHECK (org_id::text = current_setting('app.current_org_id', true));
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Drop RLS policies before dropping tables
            migrationBuilder.Sql(@"
                DROP POLICY IF EXISTS rejected_candidates_tenant_isolation ON rejected_candidates;
                DROP POLICY IF EXISTS framework_mappings_tenant_isolation ON framework_mappings;
                DROP POLICY IF EXISTS mitigations_tenant_isolation ON mitigations;
                DROP POLICY IF EXISTS threat_notes_tenant_isolation ON threat_notes;
                DROP POLICY IF EXISTS threats_tenant_isolation ON threats;
                DROP POLICY IF EXISTS arch_corrections_tenant_isolation ON architecture_corrections;
                DROP POLICY IF EXISTS arch_elements_tenant_isolation ON architecture_elements;
                DROP POLICY IF EXISTS architectures_tenant_isolation ON architectures;
            ");

            migrationBuilder.DropTable("rejected_candidates");
            migrationBuilder.DropTable("framework_mappings");
            migrationBuilder.DropTable("mitigations");
            migrationBuilder.DropTable("threat_notes");
            migrationBuilder.DropTable("threats");
            migrationBuilder.DropTable("architecture_corrections");
            migrationBuilder.DropTable("architecture_elements");
            migrationBuilder.DropTable("architectures");
        }
    }
}
