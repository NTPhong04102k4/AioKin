using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AioKin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "device_credentials",
                schema: "security",
                columns: table => new
                {
                    device_credential_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    device_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    platform = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    public_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    created_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_used_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_credentials", x => x.device_credential_id);
                    table.ForeignKey(
                        name: "fk_device_credentials_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_device_credentials_user_id_device_id",
                schema: "security",
                table: "device_credentials",
                columns: new[] { "user_id", "device_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_credentials",
                schema: "security");
        }
    }
}
