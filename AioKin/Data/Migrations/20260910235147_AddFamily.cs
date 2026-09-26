using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AioKin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFamily : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "family");

            migrationBuilder.CreateTable(
                name: "families",
                schema: "family",
                columns: table => new
                {
                    family_id = table.Column<Guid>(type: "uuid", nullable: false),
                    family_uuid = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_quota_bytes = table.Column<long>(type: "bigint", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_families", x => x.family_id);
                });

            migrationBuilder.CreateTable(
                name: "family_invites",
                schema: "family",
                columns: table => new
                {
                    family_invite_id = table.Column<Guid>(type: "uuid", nullable: false),
                    family_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    max_uses = table.Column<int>(type: "integer", nullable: false),
                    used_count = table.Column<int>(type: "integer", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_family_invites", x => x.family_invite_id);
                    table.ForeignKey(
                        name: "fk_family_invites_families_family_id",
                        column: x => x.family_id,
                        principalSchema: "family",
                        principalTable: "families",
                        principalColumn: "family_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "family_members",
                schema: "family",
                columns: table => new
                {
                    family_member_id = table.Column<Guid>(type: "uuid", nullable: false),
                    family_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    member_role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    display_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    joined_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_family_members", x => x.family_member_id);
                    table.ForeignKey(
                        name: "fk_family_members_families_family_id",
                        column: x => x.family_id,
                        principalSchema: "family",
                        principalTable: "families",
                        principalColumn: "family_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_family_members_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_families_family_uuid",
                schema: "family",
                table: "families",
                column: "family_uuid",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_family_invites_code",
                schema: "family",
                table: "family_invites",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_family_invites_family_id",
                schema: "family",
                table: "family_invites",
                column: "family_id");

            migrationBuilder.CreateIndex(
                name: "ix_family_members_family_id_user_id",
                schema: "family",
                table: "family_members",
                columns: new[] { "family_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_family_members_user_id",
                schema: "family",
                table: "family_members",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "family_invites",
                schema: "family");

            migrationBuilder.DropTable(
                name: "family_members",
                schema: "family");

            migrationBuilder.DropTable(
                name: "families",
                schema: "family");
        }
    }
}
