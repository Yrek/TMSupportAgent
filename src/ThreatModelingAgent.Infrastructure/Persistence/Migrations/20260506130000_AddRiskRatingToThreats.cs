using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThreatModelingAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRiskRatingToThreats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "risk_rating",
                table: "threats",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "risk_rating",
                table: "threats");
        }
    }
}
