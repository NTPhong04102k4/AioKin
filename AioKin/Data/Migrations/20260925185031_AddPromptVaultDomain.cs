using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AioKin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPromptVaultDomain : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "categories",
                schema: "vault",
                columns: table => new
                {
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    icon = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_categories", x => x.category_id);
                    table.ForeignKey(
                        name: "fk_categories_spaces_space_id",
                        column: x => x.space_id,
                        principalSchema: "vault",
                        principalTable: "spaces",
                        principalColumn: "space_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tags",
                schema: "vault",
                columns: table => new
                {
                    tag_id = table.Column<Guid>(type: "uuid", nullable: false),
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tags", x => x.tag_id);
                    table.ForeignKey(
                        name: "fk_tags_spaces_space_id",
                        column: x => x.space_id,
                        principalSchema: "vault",
                        principalTable: "spaces",
                        principalColumn: "space_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "prompts",
                schema: "vault",
                columns: table => new
                {
                    prompt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    author_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    is_favorite = table.Column<bool>(type: "boolean", nullable: false),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false),
                    usage_count = table.Column<int>(type: "integer", nullable: false),
                    content_size_bytes = table.Column<int>(type: "integer", nullable: true),
                    is_externalized = table.Column<bool>(type: "boolean", nullable: false),
                    content_storage_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    has_conflict = table.Column<bool>(type: "boolean", nullable: false),
                    created_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_device_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_prompts", x => x.prompt_id);
                    table.ForeignKey(
                        name: "fk_prompts_categories_category_id",
                        column: x => x.category_id,
                        principalSchema: "vault",
                        principalTable: "categories",
                        principalColumn: "category_id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_prompts_spaces_space_id",
                        column: x => x.space_id,
                        principalSchema: "vault",
                        principalTable: "spaces",
                        principalColumn: "space_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_prompts_users_author_user_id",
                        column: x => x.author_user_id,
                        principalSchema: "security",
                        principalTable: "users",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "prompt_tags",
                schema: "vault",
                columns: table => new
                {
                    prompt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tag_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_prompt_tags", x => new { x.prompt_id, x.tag_id });
                    table.ForeignKey(
                        name: "fk_prompt_tags_prompts_prompt_id",
                        column: x => x.prompt_id,
                        principalSchema: "vault",
                        principalTable: "prompts",
                        principalColumn: "prompt_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_prompt_tags_tags_tag_id",
                        column: x => x.tag_id,
                        principalSchema: "vault",
                        principalTable: "tags",
                        principalColumn: "tag_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "prompt_variables",
                schema: "vault",
                columns: table => new
                {
                    variable_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prompt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    var_key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    label = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    default_value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    var_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    options = table.Column<string>(type: "text", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_prompt_variables", x => x.variable_id);
                    table.ForeignKey(
                        name: "fk_prompt_variables_prompts_prompt_id",
                        column: x => x.prompt_id,
                        principalSchema: "vault",
                        principalTable: "prompts",
                        principalColumn: "prompt_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_categories_space_id",
                schema: "vault",
                table: "categories",
                column: "space_id");

            migrationBuilder.CreateIndex(
                name: "ix_categories_space_id_name",
                schema: "vault",
                table: "categories",
                columns: new[] { "space_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_prompt_tags_tag_id",
                schema: "vault",
                table: "prompt_tags",
                column: "tag_id");

            migrationBuilder.CreateIndex(
                name: "ix_prompt_variables_prompt_id",
                schema: "vault",
                table: "prompt_variables",
                column: "prompt_id");

            migrationBuilder.CreateIndex(
                name: "ix_prompt_variables_prompt_id_var_key",
                schema: "vault",
                table: "prompt_variables",
                columns: new[] { "prompt_id", "var_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_prompts_author_user_id",
                schema: "vault",
                table: "prompts",
                column: "author_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_prompts_category_id",
                schema: "vault",
                table: "prompts",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_prompts_space_id",
                schema: "vault",
                table: "prompts",
                column: "space_id");

            migrationBuilder.CreateIndex(
                name: "ix_prompts_space_id_is_favorite",
                schema: "vault",
                table: "prompts",
                columns: new[] { "space_id", "is_favorite" },
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_prompts_space_id_updated_date",
                schema: "vault",
                table: "prompts",
                columns: new[] { "space_id", "updated_date" },
                filter: "is_deleted = false");

            migrationBuilder.CreateIndex(
                name: "ix_tags_space_id",
                schema: "vault",
                table: "tags",
                column: "space_id");

            migrationBuilder.CreateIndex(
                name: "ix_tags_space_id_name",
                schema: "vault",
                table: "tags",
                columns: new[] { "space_id", "name" },
                unique: true);

            // FTS: HasGeneratedTsVectorColumn khong khop cach dung to_tsvector truc tiep
            // tren 2 cot (title || ' ' || content) ma khong luu them cot moi, nen sinh
            // bang raw SQL o day (Step 6 cua Task 5).
            migrationBuilder.Sql(
                "CREATE INDEX ix_prompts_fts ON vault.prompts USING GIN (to_tsvector('simple', title || ' ' || content));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS vault.ix_prompts_fts;");

            migrationBuilder.DropTable(
                name: "prompt_tags",
                schema: "vault");

            migrationBuilder.DropTable(
                name: "prompt_variables",
                schema: "vault");

            migrationBuilder.DropTable(
                name: "tags",
                schema: "vault");

            migrationBuilder.DropTable(
                name: "prompts",
                schema: "vault");

            migrationBuilder.DropTable(
                name: "categories",
                schema: "vault");
        }
    }
}
