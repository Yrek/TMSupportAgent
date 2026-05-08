using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThreatModelingAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAcceptanceCriteriaToMitigations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "acceptance_criteria",
                table: "mitigations",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "acceptance_criteria",
                table: "mitigations");
        }
    }
}
