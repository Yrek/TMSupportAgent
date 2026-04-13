using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThreatModelingAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrgSuspension : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_suspended",
                table: "organizations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "suspended_at",
                table: "organizations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "architectures",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    classification = table.Column<string[]>(type: "text[]", nullable: false),
                    system_purpose = table.Column<string>(type: "text", nullable: true),
                    assumptions = table.Column<string>(type: "jsonb", nullable: false),
                    gaps = table.Column<string>(type: "jsonb", nullable: false),
                    clarification_questions = table.Column<string>(type: "jsonb", nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    confirmed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_architectures", x => x.id);
                });

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
                });

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
                    confidence = table.Column<string>(type: "text", nullable: false),
                    evidence_basis = table.Column<string[]>(type: "text[]", nullable: false),
                    evidence_strength = table.Column<string>(type: "text", nullable: false),
                    assumptions = table.Column<string>(type: "text", nullable: true),
                    finding_type = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_threats", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "architecture_elements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    architecture_id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    element_type = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    properties = table.Column<string>(type: "jsonb", nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    extraction_confidence = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_architecture_elements", x => x.id);
                    table.ForeignKey(
                        name: "FK_architecture_elements_architectures_architecture_id",
                        column: x => x.architecture_id,
                        principalTable: "architectures",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

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
                    table.ForeignKey(
                        name: "FK_framework_mappings_threats_threat_id",
                        column: x => x.threat_id,
                        principalTable: "threats",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

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
                    table.ForeignKey(
                        name: "FK_mitigations_threats_threat_id",
                        column: x => x.threat_id,
                        principalTable: "threats",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

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
                    table.ForeignKey(
                        name: "FK_threat_notes_threats_threat_id",
                        column: x => x.threat_id,
                        principalTable: "threats",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "architecture_corrections",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    element_id = table.Column<Guid>(type: "uuid", nullable: true),
                    architecture_id = table.Column<Guid>(type: "uuid", nullable: false),
                    org_id = table.Column<Guid>(type: "uuid", nullable: false),
                    corrected_by = table.Column<Guid>(type: "uuid", nullable: false),
                    correction_type = table.Column<string>(type: "text", nullable: false),
                    field_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    original_value = table.Column<string>(type: "text", nullable: true),
                    corrected_value = table.Column<string>(type: "text", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_architecture_corrections", x => x.id);
                    table.ForeignKey(
                        name: "FK_architecture_corrections_architecture_elements_element_id",
                        column: x => x.element_id,
                        principalTable: "architecture_elements",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_architecture_corrections_architectures_architecture_id",
                        column: x => x.architecture_id,
                        principalTable: "architectures",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_architecture_corrections_architecture_id",
                table: "architecture_corrections",
                column: "architecture_id");

            migrationBuilder.CreateIndex(
                name: "IX_architecture_corrections_element_id",
                table: "architecture_corrections",
                column: "element_id");

            migrationBuilder.CreateIndex(
                name: "IX_architecture_elements_architecture_id",
                table: "architecture_elements",
                column: "architecture_id");

            migrationBuilder.CreateIndex(
                name: "IX_architecture_elements_architecture_id_element_type",
                table: "architecture_elements",
                columns: new[] { "architecture_id", "element_type" });

            migrationBuilder.CreateIndex(
                name: "IX_architectures_job_id",
                table: "architectures",
                column: "job_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_architectures_org_id",
                table: "architectures",
                column: "org_id");

            migrationBuilder.CreateIndex(
                name: "IX_framework_mappings_threat_id",
                table: "framework_mappings",
                column: "threat_id");

            migrationBuilder.CreateIndex(
                name: "IX_mitigations_threat_id",
                table: "mitigations",
                column: "threat_id");

            migrationBuilder.CreateIndex(
                name: "IX_rejected_candidates_job_id",
                table: "rejected_candidates",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "IX_threat_notes_threat_id",
                table: "threat_notes",
                column: "threat_id");

            migrationBuilder.CreateIndex(
                name: "IX_threats_job_id",
                table: "threats",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "IX_threats_job_id_finding_type",
                table: "threats",
                columns: new[] { "job_id", "finding_type" });

            migrationBuilder.CreateIndex(
                name: "IX_threats_job_id_identifier",
                table: "threats",
                columns: new[] { "job_id", "identifier" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "architecture_corrections");

            migrationBuilder.DropTable(
                name: "framework_mappings");

            migrationBuilder.DropTable(
                name: "mitigations");

            migrationBuilder.DropTable(
                name: "rejected_candidates");

            migrationBuilder.DropTable(
                name: "threat_notes");

            migrationBuilder.DropTable(
                name: "architecture_elements");

            migrationBuilder.DropTable(
                name: "threats");

            migrationBuilder.DropTable(
                name: "architectures");

            migrationBuilder.DropColumn(
                name: "is_suspended",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "suspended_at",
                table: "organizations");
        }
    }
}
