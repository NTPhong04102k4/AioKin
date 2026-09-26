using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AioKin.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSyncLogOriginUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "origin_user_id",
                schema: "sync",
                table: "sync_log",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "updated_by_user_id",
                schema: "vault",
                table: "prompts",
                type: "uuid",
                nullable: true);

            // Carry-forward Task 3 (progress.md): echo suppression keyed tren origin_device_id
            // MOT MINH la spoofable — 2 thanh vien KHAC NHAU trong cung mot space chia se co
            // the tu chon trung DeviceInfo.DeviceId (chuoi client tu dat luc dang nhap). Trigger
            // gio ghi kem origin_user_id (tu vault.prompts.updated_by_user_id, duoc SyncService
            // gan cung luc voi updated_device_id) de pull suppress dung tren CAP (user, device).
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION sync.fn_prompts_write_log()
                RETURNS TRIGGER AS $$
                BEGIN
                    INSERT INTO sync.sync_log (space_id, entity_type, entity_id, operation, payload, origin_device_id, origin_user_id, version)
                    VALUES (
                        COALESCE(NEW.space_id, OLD.space_id),
                        'prompt',
                        COALESCE(NEW.prompt_id, OLD.prompt_id),
                        LOWER(TG_OP),
                        CASE WHEN TG_OP = 'DELETE' THEN to_jsonb(OLD) ELSE to_jsonb(NEW) END,
                        COALESCE(NEW.updated_device_id, OLD.updated_device_id),
                        COALESCE(NEW.updated_by_user_id, OLD.updated_by_user_id),
                        COALESCE(NEW.version, OLD.version)
                    );
                    RETURN COALESCE(NEW, OLD);
                END;
                $$ LANGUAGE plpgsql;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Khoi phuc dinh nghia truoc migration nay (khong ghi origin_user_id) TRUOC khi xoa
            // cot — neu khong ham se tham chieu mot cot khong con ton tai.
            migrationBuilder.Sql("""
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
                """);

            migrationBuilder.DropColumn(
                name: "origin_user_id",
                schema: "sync",
                table: "sync_log");

            migrationBuilder.DropColumn(
                name: "updated_by_user_id",
                schema: "vault",
                table: "prompts");
        }
    }
}
