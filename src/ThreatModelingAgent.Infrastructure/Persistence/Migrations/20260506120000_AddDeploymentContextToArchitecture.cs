using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ThreatModelingAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDeploymentContextToArchitecture : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "deployment_context",
                table: "architectures",
                type: "jsonb",
                nullable: false,
                defaultValue: "{}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "deployment_context",
                table: "architectures");
        }
    }
}
