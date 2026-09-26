using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AioKin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSyncEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "sync");

            migrationBuilder.CreateTable(
                name: "backup_snapshots",
                schema: "sync",
                columns: table => new
                {
                    snapshot_id = table.Column<Guid>(type: "uuid", nullable: false),
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    triggered_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    snapshot_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    storage_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    file_size_bytes = table.Column<long>(type: "bigint", nullable: true),
                    prompt_count = table.Column<int>(type: "integer", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_backup_snapshots", x => x.snapshot_id);
                });

            migrationBuilder.CreateTable(
                name: "devices",
                schema: "sync",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    device_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    device_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    platform = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    app_version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    last_synced_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    is_stale = table.Column<bool>(type: "boolean", nullable: false),
                    created_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_devices", x => new { x.user_id, x.device_id });
                });

            migrationBuilder.CreateTable(
                name: "sync_conflicts",
                schema: "sync",
                columns: table => new
                {
                    conflict_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    local_payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    remote_payload_json = table.Column<string>(type: "jsonb", nullable: false),
                    local_version = table.Column<int>(type: "integer", nullable: false),
                    remote_version = table.Column<int>(type: "integer", nullable: false),
                    resolved = table.Column<bool>(type: "boolean", nullable: false),
                    resolution_strategy = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    resolved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sync_conflicts", x => x.conflict_id);
                });

            migrationBuilder.CreateTable(
                name: "sync_log",
                schema: "sync",
                columns: table => new
                {
                    sync_log_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    space_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: true),
                    origin_device_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sync_log", x => x.sync_log_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_backup_snapshots_space_id_created_at",
                schema: "sync",
                table: "backup_snapshots",
                columns: new[] { "space_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_devices_last_synced_at",
                schema: "sync",
                table: "devices",
                column: "last_synced_at",
                filter: "is_stale = false");

            migrationBuilder.CreateIndex(
                name: "ix_sync_conflicts_entity_type_entity_id",
                schema: "sync",
                table: "sync_conflicts",
                columns: new[] { "entity_type", "entity_id" },
                filter: "resolved = false");

            migrationBuilder.CreateIndex(
                name: "ix_sync_log_entity_type_entity_id",
                schema: "sync",
                table: "sync_log",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_sync_log_space_id_created_at",
                schema: "sync",
                table: "sync_log",
                columns: new[] { "space_id", "created_at" });

            // Trigger 1: bump version + updated_date CHI khi co thay doi noi dung that su.
            // WHEN co them is_deleted (P11) — soft-delete phai bump version giong moi write
            // khac, de mot edit den tren base_version cu sau khi prompt bi xoa duoc nhan dung
            // la conflict thay vi am tham ap vao mot dong da "chet". is_favorite/has_conflict
            // KHONG nam trong WHEN — bat/tat rieng chung khong duoc tinh la mot sua doi noi dung.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION vault.fn_prompts_before_update()
                RETURNS TRIGGER AS $$
                BEGIN
                    NEW.version := OLD.version + 1;
                    NEW.updated_date := now();
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER trg_prompts_before_update
                    BEFORE UPDATE ON vault.prompts
                    FOR EACH ROW
                    WHEN (
                        OLD.title IS DISTINCT FROM NEW.title OR
                        OLD.content IS DISTINCT FROM NEW.content OR
                        OLD.description IS DISTINCT FROM NEW.description OR
                        OLD.category_id IS DISTINCT FROM NEW.category_id OR
                        OLD.is_deleted IS DISTINCT FROM NEW.is_deleted
                    )
                    EXECUTE FUNCTION vault.fn_prompts_before_update();

                CREATE OR REPLACE FUNCTION sync.fn_prompts_write_log()
                RETURNS TRIGGER AS $$
                BEGIN
                    INSERT INTO sync.sync_log (space_id, entity_type, entity_id, operation, payload, origin_device_id, version)
                    VALUES (
                        COALESCE(NEW.space_id, OLD.space_id),
                        'prompt',
                        COALESCE(NEW.prompt_id, OLD.prompt_id),
                        LOWER(TG_OP),
                        CASE WHEN TG_OP = 'DELETE' THEN to_jsonb(OLD) ELSE to_jsonb(NEW) END,
                        COALESCE(NEW.updated_device_id, OLD.updated_device_id),
                        COALESCE(NEW.version, OLD.version)
                    );
                    RETURN COALESCE(NEW, OLD);
                END;
                $$ LANGUAGE plpgsql;

                -- Trigger 2: ghi sync_log. Postgres KHONG cho WHEN cua mot trigger INSERT
                -- (ke ca khi gop chung voi UPDATE/DELETE trong mot CREATE TRIGGER) tham chieu
                -- OLD ("42P17: INSERT trigger's WHEN condition cannot reference OLD values"),
                -- va cung khong cho WHEN dung bien TG_OP ("42703: column tg_op does not
                -- exist" — TG_OP chi ton tai trong than ham PL/pgSQL). Vi vay tach thanh 3
                -- trigger rieng theo tung loai thao tac thay vi 1 trigger gop dieu kien TG_OP:
                --   - INSERT/DELETE: luon ghi log, khong dieu kien.
                --   - UPDATE: CHI ghi khi version thuc su tang (P10) — vi trigger 1 chi bump
                --     version tren thay doi noi dung that su (bao gom ca is_deleted, P11), dieu
                --     kien "version co doi" tuong duong voi "day la mot content change that",
                --     nen mot write CHI bat has_conflict/is_favorite (khong lam version nhich
                --     len) se khong con tao them dong sync_log gia.
                CREATE TRIGGER trg_prompts_write_sync_log_ins
                    AFTER INSERT ON vault.prompts
                    FOR EACH ROW
                    EXECUTE FUNCTION sync.fn_prompts_write_log();

                CREATE TRIGGER trg_prompts_write_sync_log_upd
                    AFTER UPDATE ON vault.prompts
                    FOR EACH ROW
                    WHEN (OLD.version IS DISTINCT FROM NEW.version)
                    EXECUTE FUNCTION sync.fn_prompts_write_log();

                CREATE TRIGGER trg_prompts_write_sync_log_del
                    AFTER DELETE ON vault.prompts
                    FOR EACH ROW
                    EXECUTE FUNCTION sync.fn_prompts_write_log();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_prompts_write_sync_log_ins ON vault.prompts;
                DROP TRIGGER IF EXISTS trg_prompts_write_sync_log_upd ON vault.prompts;
                DROP TRIGGER IF EXISTS trg_prompts_write_sync_log_del ON vault.prompts;
                DROP FUNCTION IF EXISTS sync.fn_prompts_write_log();
                DROP TRIGGER IF EXISTS trg_prompts_before_update ON vault.prompts;
                DROP FUNCTION IF EXISTS vault.fn_prompts_before_update();
                """);

            migrationBuilder.DropTable(
                name: "backup_snapshots",
                schema: "sync");

            migrationBuilder.DropTable(
                name: "devices",
                schema: "sync");

            migrationBuilder.DropTable(
                name: "sync_conflicts",
                schema: "sync");

            migrationBuilder.DropTable(
                name: "sync_log",
                schema: "sync");
        }
    }
}
