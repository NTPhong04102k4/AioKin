using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AioKin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPromptVaultSpaces : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "promptvault");

            migrationBuilder.CreateTable(
                name: "spaces",
                schema: "promptvault",
                columns: table => new
                {
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    space_uuid = table.Column<Guid>(type: "uuid", nullable: false),
                    space_type = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    family_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_spaces", x => x.space_id);
                    table.ForeignKey(
                        name: "fk_spaces_families_family_id",
                        column: x => x.family_id,
                        principalSchema: "family",
                        principalTable: "families",
                        principalColumn: "family_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_spaces_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "space_members",
                schema: "promptvault",
                columns: table => new
                {
                    space_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_role = table.Column<int>(type: "integer", nullable: false),
                    joined_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_space_members", x => x.space_member_id);
                    table.ForeignKey(
                        name: "fk_space_members_spaces_space_id",
                        column: x => x.space_id,
                        principalSchema: "promptvault",
                        principalTable: "spaces",
                        principalColumn: "space_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_space_members_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_space_members_space_id_user_id",
                schema: "promptvault",
                table: "space_members",
                columns: new[] { "space_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_space_members_user_id",
                schema: "promptvault",
                table: "space_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_spaces_family_id",
                schema: "promptvault",
                table: "spaces",
                column: "family_id",
                unique: true,
                filter: "family_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_spaces_owner_user_id",
                schema: "promptvault",
                table: "spaces",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_spaces_space_uuid",
                schema: "promptvault",
                table: "spaces",
                column: "space_uuid",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "space_members",
                schema: "promptvault");

            migrationBuilder.DropTable(
                name: "spaces",
                schema: "promptvault");
        }
    }
}
