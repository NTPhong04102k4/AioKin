using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AioKin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSyncConflictOperationMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "local_operation",
                schema: "sync",
                table: "sync_conflicts",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "remote_is_deleted",
                schema: "sync",
                table: "sync_conflicts",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "space_id",
                schema: "sync",
                table: "sync_conflicts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "local_operation",
                schema: "sync",
                table: "sync_conflicts");

            migrationBuilder.DropColumn(
                name: "remote_is_deleted",
                schema: "sync",
                table: "sync_conflicts");

            migrationBuilder.DropColumn(
                name: "space_id",
                schema: "sync",
                table: "sync_conflicts");
        }
    }
}
