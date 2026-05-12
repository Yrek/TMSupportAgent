using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThreatModelingAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEntraTenantId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "entra_tenant_id",
                table: "organizations",
                type: "character varying(36)",
                maxLength: 36,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_organizations_entra_tenant_id",
                table: "organizations",
                column: "entra_tenant_id",
                unique: true,
                filter: "entra_tenant_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_organizations_entra_tenant_id",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "entra_tenant_id",
                table: "organizations");
        }
    }
}
