using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCycles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "cycle_default_duration_days",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 14);

            migrationBuilder.AddColumn<bool>(
                name: "carried_over",
                table: "tasks",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "cycles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    start_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    end_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'planned'"),
                    version = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cycles", x => x.id);
                });

            migrationBuilder.UpdateData(
                table: "users",
                keyColumn: "id",
                keyValue: new Guid("00000000-0000-0000-0000-000000000000"),
                column: "cycle_default_duration_days",
                value: 14);

            migrationBuilder.CreateIndex(
                name: "ix_cycles_ordering",
                table: "cycles",
                columns: new[] { "start_date", "created_at", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_cycles_single_active",
                table: "cycles",
                column: "status",
                unique: true,
                filter: "status = 'active'");

            // Hand-authored (slice 011 D1/D2): tasks.cycle_id has existed since AddTasks as a
            // reserved raw uuid column and DELIBERATELY stays unmodeled as an EF relationship
            // (the raw Guid? FK cannot pair with the value-converted CycleId PK), so the
            // constraint + its index are added here directly. ON DELETE RESTRICT backs the
            // FR-020 delete guard at the database level.
            migrationBuilder.CreateIndex(
                name: "IX_tasks_cycle_id",
                table: "tasks",
                column: "cycle_id");

            migrationBuilder.AddForeignKey(
                name: "FK_tasks_cycles_cycle_id",
                table: "tasks",
                column: "cycle_id",
                principalTable: "cycles",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_tasks_cycles_cycle_id",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "IX_tasks_cycle_id",
                table: "tasks");

            migrationBuilder.DropTable(
                name: "cycles");

            migrationBuilder.DropColumn(
                name: "cycle_default_duration_days",
                table: "users");

            migrationBuilder.DropColumn(
                name: "carried_over",
                table: "tasks");
        }
    }
}
