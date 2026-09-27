using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AioKin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPromptMetaSig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "meta_sig",
                schema: "promptvault",
                table: "prompts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            // Follow-up (tag/variable-only versioning gap): them meta_sig VAO WHEN clause cua
            // trg_prompts_before_update (dinh nghia goc trong migration AddSyncEngine) — chi can
            // CREATE OR REPLACE lai function (khong doi than) roi DROP + CREATE TRIGGER lai voi
            // WHEN moi (Postgres khong cho ALTER TRIGGER ... WHEN, phai tao lai). Cac dieu kien
            // cu (title/content/description/category_id/is_deleted) giu nguyen, chi THEM dieu
            // kien meta_sig — mot thay doi CHI tag/variable (truoc day trigger hoan toan "mu"
            // voi loai thay doi nay, xem SyncService.ComputeMetaSig) gio se cung bump version +
            // ghi sync_log giong het mot content change that su.
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_prompts_before_update ON promptvault.prompts;

                CREATE OR REPLACE FUNCTION promptvault.fn_prompts_before_update()
                RETURNS TRIGGER AS $$
                BEGIN
                    NEW.version := OLD.version + 1;
                    NEW.updated_date := now();
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER trg_prompts_before_update
                    BEFORE UPDATE ON promptvault.prompts
                    FOR EACH ROW
                    WHEN (
                        OLD.title IS DISTINCT FROM NEW.title OR
                        OLD.content IS DISTINCT FROM NEW.content OR
                        OLD.description IS DISTINCT FROM NEW.description OR
                        OLD.category_id IS DISTINCT FROM NEW.category_id OR
                        OLD.is_deleted IS DISTINCT FROM NEW.is_deleted OR
                        OLD.meta_sig IS DISTINCT FROM NEW.meta_sig
                    )
                    EXECUTE FUNCTION promptvault.fn_prompts_before_update();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Khoi phuc lai WHEN clause CU (khong co meta_sig) TRUOC khi drop cot — trigger phai
            // con hop le (khong tham chieu cot sap bi xoa) tai moi thoi diem trong Down().
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_prompts_before_update ON promptvault.prompts;

                CREATE OR REPLACE FUNCTION promptvault.fn_prompts_before_update()
                RETURNS TRIGGER AS $$
                BEGIN
                    NEW.version := OLD.version + 1;
                    NEW.updated_date := now();
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER trg_prompts_before_update
                    BEFORE UPDATE ON promptvault.prompts
                    FOR EACH ROW
                    WHEN (
                        OLD.title IS DISTINCT FROM NEW.title OR
                        OLD.content IS DISTINCT FROM NEW.content OR
                        OLD.description IS DISTINCT FROM NEW.description OR
                        OLD.category_id IS DISTINCT FROM NEW.category_id OR
                        OLD.is_deleted IS DISTINCT FROM NEW.is_deleted
                    )
                    EXECUTE FUNCTION promptvault.fn_prompts_before_update();
                """);

            migrationBuilder.DropColumn(
                name: "meta_sig",
                schema: "promptvault",
                table: "prompts");
        }
    }
}
