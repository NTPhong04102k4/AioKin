using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AioKin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSpacesPersonalUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_spaces_owner_user_id",
                schema: "vault",
                table: "spaces");

            migrationBuilder.CreateIndex(
                name: "ix_spaces_owner_personal_unique",
                schema: "vault",
                table: "spaces",
                column: "owner_user_id",
                unique: true,
                filter: "space_type = 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_spaces_owner_personal_unique",
                schema: "vault",
                table: "spaces");

            migrationBuilder.CreateIndex(
                name: "ix_spaces_owner_user_id",
                schema: "vault",
                table: "spaces",
                column: "owner_user_id");
        }
    }
}
