using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AioKin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscoveryAndScheduleItems : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "discovery_items",
                schema: "core",
                columns: table => new
                {
                    discovery_item_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    author_user_id = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    category = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reading_minutes = table.Column<int>(type: "integer", nullable: false),
                    is_published = table.Column<bool>(type: "boolean", nullable: false),
                    created_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discovery_items", x => x.discovery_item_id);
                });

            migrationBuilder.CreateTable(
                name: "schedule_items",
                schema: "core",
                columns: table => new
                {
                    schedule_item_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    start_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_done = table.Column<bool>(type: "boolean", nullable: false),
                    created_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_schedule_items", x => x.schedule_item_id);
                    table.ForeignKey(
                        name: "fk_schedule_items_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_discovery_items_category",
                schema: "core",
                table: "discovery_items",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "ix_discovery_items_is_published_created_date",
                schema: "core",
                table: "discovery_items",
                columns: new[] { "is_published", "created_date" });

            migrationBuilder.CreateIndex(
                name: "ix_schedule_items_user_id_start_at",
                schema: "core",
                table: "schedule_items",
                columns: new[] { "user_id", "start_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "discovery_items",
                schema: "core");

            migrationBuilder.DropTable(
                name: "schedule_items",
                schema: "core");
        }
    }
}
