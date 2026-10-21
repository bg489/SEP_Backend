using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoScenarioX.AuthApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class WorkflowDomainTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "analytics_configs",
                schema: "app_workflow",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_analytics_configs", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "audit_logs",
                schema: "app_workflow",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_logs", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "campaign_rankings",
                schema: "app_workflow",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_campaign_rankings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "campaigns",
                schema: "app_workflow",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_campaigns", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "carla_integrations",
                schema: "app_workflow",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_carla_integrations", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "failure_clusters",
                schema: "app_workflow",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_failure_clusters", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "oracle_profiles",
                schema: "app_workflow",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_oracle_profiles", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "oracle_rules",
                schema: "app_workflow",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_oracle_rules", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "run_artifacts",
                schema: "app_workflow",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_run_artifacts", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "scenario_artifacts",
                schema: "app_workflow",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scenario_artifacts", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "scenario_runs",
                schema: "app_workflow",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scenario_runs", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "scenario_versions",
                schema: "app_workflow",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scenario_versions", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "scenarios",
                schema: "app_workflow",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scenarios", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "simulation_nodes",
                schema: "app_workflow",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_simulation_nodes", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_analytics_configs_UpdatedAt",
                schema: "app_workflow",
                table: "analytics_configs",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_audit_logs_UpdatedAt",
                schema: "app_workflow",
                table: "audit_logs",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_campaign_rankings_UpdatedAt",
                schema: "app_workflow",
                table: "campaign_rankings",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_campaigns_UpdatedAt",
                schema: "app_workflow",
                table: "campaigns",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_carla_integrations_UpdatedAt",
                schema: "app_workflow",
                table: "carla_integrations",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_failure_clusters_UpdatedAt",
                schema: "app_workflow",
                table: "failure_clusters",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_oracle_profiles_UpdatedAt",
                schema: "app_workflow",
                table: "oracle_profiles",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_oracle_rules_UpdatedAt",
                schema: "app_workflow",
                table: "oracle_rules",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_run_artifacts_UpdatedAt",
                schema: "app_workflow",
                table: "run_artifacts",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_scenario_artifacts_UpdatedAt",
                schema: "app_workflow",
                table: "scenario_artifacts",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_scenario_runs_UpdatedAt",
                schema: "app_workflow",
                table: "scenario_runs",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_scenario_versions_UpdatedAt",
                schema: "app_workflow",
                table: "scenario_versions",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_scenarios_UpdatedAt",
                schema: "app_workflow",
                table: "scenarios",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_simulation_nodes_UpdatedAt",
                schema: "app_workflow",
                table: "simulation_nodes",
                column: "UpdatedAt");

            migrationBuilder.Sql("""
                INSERT INTO app_workflow.scenarios ("Key", "Payload", "UpdatedAt") SELECT "Key", "Payload", "UpdatedAt" FROM app_workflow.documents WHERE "Area" = 'scenario';
                INSERT INTO app_workflow.scenario_versions ("Key", "Payload", "UpdatedAt") SELECT "Key", "Payload", "UpdatedAt" FROM app_workflow.documents WHERE "Area" = 'scenario-versions';
                INSERT INTO app_workflow.scenario_artifacts ("Key", "Payload", "UpdatedAt") SELECT "Key", "Payload", "UpdatedAt" FROM app_workflow.documents WHERE "Area" = 'scenario-artifact';
                INSERT INTO app_workflow.campaigns ("Key", "Payload", "UpdatedAt") SELECT "Key", "Payload", "UpdatedAt" FROM app_workflow.documents WHERE "Area" = 'campaign';
                INSERT INTO app_workflow.campaign_rankings ("Key", "Payload", "UpdatedAt") SELECT "Key", "Payload", "UpdatedAt" FROM app_workflow.documents WHERE "Area" = 'campaign-ranking';
                INSERT INTO app_workflow.scenario_runs ("Key", "Payload", "UpdatedAt") SELECT "Key", "Payload", "UpdatedAt" FROM app_workflow.documents WHERE "Area" = 'run';
                INSERT INTO app_workflow.run_artifacts ("Key", "Payload", "UpdatedAt") SELECT "Key", "Payload", "UpdatedAt" FROM app_workflow.documents WHERE "Area" = 'run-artifact';
                INSERT INTO app_workflow.failure_clusters ("Key", "Payload", "UpdatedAt") SELECT "Key", "Payload", "UpdatedAt" FROM app_workflow.documents WHERE "Area" = 'failure';
                INSERT INTO app_workflow.analytics_configs ("Key", "Payload", "UpdatedAt") SELECT "Key", "Payload", "UpdatedAt" FROM app_workflow.documents WHERE "Area" = 'analytics';
                INSERT INTO app_workflow.audit_logs ("Key", "Payload", "UpdatedAt") SELECT "Key", "Payload", "UpdatedAt" FROM app_workflow.documents WHERE "Area" = 'audit';
                INSERT INTO app_workflow.carla_integrations ("Key", "Payload", "UpdatedAt") SELECT "Key", "Payload", "UpdatedAt" FROM app_workflow.documents WHERE "Area" = 'integration';
                INSERT INTO app_workflow.simulation_nodes ("Key", "Payload", "UpdatedAt") SELECT "Key", "Payload", "UpdatedAt" FROM app_workflow.documents WHERE "Area" = 'worker';
                INSERT INTO app_workflow.oracle_rules ("Key", "Payload", "UpdatedAt") SELECT "Key", "Payload", "UpdatedAt" FROM app_workflow.documents WHERE "Area" = 'oracle-rule';
                INSERT INTO app_workflow.oracle_profiles ("Key", "Payload", "UpdatedAt") SELECT "Key", "Payload", "UpdatedAt" FROM app_workflow.documents WHERE "Area" = 'oracle-profile';
                """);

            migrationBuilder.DropTable(
                name: "documents",
                schema: "app_workflow");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "analytics_configs",
                schema: "app_workflow");

            migrationBuilder.DropTable(
                name: "audit_logs",
                schema: "app_workflow");

            migrationBuilder.DropTable(
                name: "campaign_rankings",
                schema: "app_workflow");

            migrationBuilder.DropTable(
                name: "campaigns",
                schema: "app_workflow");

            migrationBuilder.DropTable(
                name: "carla_integrations",
                schema: "app_workflow");

            migrationBuilder.DropTable(
                name: "failure_clusters",
                schema: "app_workflow");

            migrationBuilder.DropTable(
                name: "oracle_profiles",
                schema: "app_workflow");

            migrationBuilder.DropTable(
                name: "oracle_rules",
                schema: "app_workflow");

            migrationBuilder.DropTable(
                name: "run_artifacts",
                schema: "app_workflow");

            migrationBuilder.DropTable(
                name: "scenario_artifacts",
                schema: "app_workflow");

            migrationBuilder.DropTable(
                name: "scenario_runs",
                schema: "app_workflow");

            migrationBuilder.DropTable(
                name: "scenario_versions",
                schema: "app_workflow");

            migrationBuilder.DropTable(
                name: "scenarios",
                schema: "app_workflow");

            migrationBuilder.DropTable(
                name: "simulation_nodes",
                schema: "app_workflow");

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

            migrationBuilder.Sql("""
                INSERT INTO app_workflow.documents ("Area", "Key", "Payload", "UpdatedAt") SELECT 'scenario', "Key", "Payload", "UpdatedAt" FROM app_workflow.scenarios;
                INSERT INTO app_workflow.documents ("Area", "Key", "Payload", "UpdatedAt") SELECT 'scenario-versions', "Key", "Payload", "UpdatedAt" FROM app_workflow.scenario_versions;
                INSERT INTO app_workflow.documents ("Area", "Key", "Payload", "UpdatedAt") SELECT 'scenario-artifact', "Key", "Payload", "UpdatedAt" FROM app_workflow.scenario_artifacts;
                INSERT INTO app_workflow.documents ("Area", "Key", "Payload", "UpdatedAt") SELECT 'campaign', "Key", "Payload", "UpdatedAt" FROM app_workflow.campaigns;
                INSERT INTO app_workflow.documents ("Area", "Key", "Payload", "UpdatedAt") SELECT 'campaign-ranking', "Key", "Payload", "UpdatedAt" FROM app_workflow.campaign_rankings;
                INSERT INTO app_workflow.documents ("Area", "Key", "Payload", "UpdatedAt") SELECT 'run', "Key", "Payload", "UpdatedAt" FROM app_workflow.scenario_runs;
                INSERT INTO app_workflow.documents ("Area", "Key", "Payload", "UpdatedAt") SELECT 'run-artifact', "Key", "Payload", "UpdatedAt" FROM app_workflow.run_artifacts;
                INSERT INTO app_workflow.documents ("Area", "Key", "Payload", "UpdatedAt") SELECT 'failure', "Key", "Payload", "UpdatedAt" FROM app_workflow.failure_clusters;
                INSERT INTO app_workflow.documents ("Area", "Key", "Payload", "UpdatedAt") SELECT 'analytics', "Key", "Payload", "UpdatedAt" FROM app_workflow.analytics_configs;
                INSERT INTO app_workflow.documents ("Area", "Key", "Payload", "UpdatedAt") SELECT 'audit', "Key", "Payload", "UpdatedAt" FROM app_workflow.audit_logs;
                INSERT INTO app_workflow.documents ("Area", "Key", "Payload", "UpdatedAt") SELECT 'integration', "Key", "Payload", "UpdatedAt" FROM app_workflow.carla_integrations;
                INSERT INTO app_workflow.documents ("Area", "Key", "Payload", "UpdatedAt") SELECT 'worker', "Key", "Payload", "UpdatedAt" FROM app_workflow.simulation_nodes;
                INSERT INTO app_workflow.documents ("Area", "Key", "Payload", "UpdatedAt") SELECT 'oracle-rule', "Key", "Payload", "UpdatedAt" FROM app_workflow.oracle_rules;
                INSERT INTO app_workflow.documents ("Area", "Key", "Payload", "UpdatedAt") SELECT 'oracle-profile', "Key", "Payload", "UpdatedAt" FROM app_workflow.oracle_profiles;
                """);
        }
    }
}
