using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TaskFlow.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRecurrenceLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "spawned_from_id",
                table: "tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_tasks_spawned_from_live",
                table: "tasks",
                column: "spawned_from_id",
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_tasks_tasks_spawned_from_id",
                table: "tasks",
                column: "spawned_from_id",
                principalTable: "tasks",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_tasks_tasks_spawned_from_id",
                table: "tasks");

            migrationBuilder.DropIndex(
                name: "ux_tasks_spawned_from_live",
                table: "tasks");

            migrationBuilder.DropColumn(
                name: "spawned_from_id",
                table: "tasks");
        }
    }
}
