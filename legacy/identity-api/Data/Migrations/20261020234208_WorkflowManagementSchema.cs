using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoScenarioX.AuthApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowManagementSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "app_workflow");

            migrationBuilder.CreateTable(
                name: "documents",
                schema: "app_workflow",
                columns: table => new
                {
                    Area = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_documents", x => new { x.Area, x.Key });
                });

            migrationBuilder.CreateIndex(
                name: "IX_documents_Area_UpdatedAt",
                schema: "app_workflow",
                table: "documents",
                columns: new[] { "Area", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "documents",
                schema: "app_workflow");
        }
    }
}
