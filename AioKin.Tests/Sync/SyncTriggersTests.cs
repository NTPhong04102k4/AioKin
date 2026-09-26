using AioKin.Data;
using AioKin.Data.Entities.Vault;
using AioKin.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace AioKin.Tests.Sync;

/// <summary>
/// Kiem tra truc tiep 2 trigger Postgres (promptvault.fn_prompts_before_update va
/// sync.fn_prompts_write_log) bang raw SQL qua chinh ket noi cua DbContext — khong di qua
/// EF SaveChanges — de xac nhan hanh vi la cua DATABASE, khong phai cua tang C#.
/// </summary>
[Collection(ApiCollection.Name)]
public class SyncTriggersTests
{
    private readonly ApiFixture _fixture;

    public SyncTriggersTests(ApiFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Content_update_bump_version_va_ghi_dung_1_dong_sync_log()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);

        var (user, space) = await SeedUserAndSpaceAsync(db);
        var promptId = Guid.NewGuid();

        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();

        await ExecAsync(conn, """
            INSERT INTO promptvault.prompts (prompt_id, space_id, author_user_id, title, content,
                is_favorite, is_archived, usage_count, is_externalized, version, is_deleted,
                has_conflict, created_date, updated_date)
            VALUES (@p, @s, @u, 'T1', 'C1', false, false, 0, false, 1, false, false, now(), now());
            """, ("p", promptId), ("s", space), ("u", user));

        Assert.Equal(1, await CountSyncLogAsync(conn, promptId));

        await ExecAsync(conn, "UPDATE promptvault.prompts SET content = 'C2' WHERE prompt_id = @p;", ("p", promptId));

        Assert.Equal(2, await GetVersionAsync(conn, promptId));
        Assert.Equal(2, await CountSyncLogAsync(conn, promptId));

        var ops = await GetOperationsAsync(conn, promptId);
        Assert.Equal(new[] { "insert", "update" }, ops);
    }

    [Fact]
    public async Task Has_conflict_hoac_is_favorite_rieng_le_khong_bump_version_va_khong_ghi_sync_log()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);

        var (user, space) = await SeedUserAndSpaceAsync(db);
        var promptId = Guid.NewGuid();

        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();

        await ExecAsync(conn, """
            INSERT INTO promptvault.prompts (prompt_id, space_id, author_user_id, title, content,
                is_favorite, is_archived, usage_count, is_externalized, version, is_deleted,
                has_conflict, created_date, updated_date)
            VALUES (@p, @s, @u, 'T1', 'C1', false, false, 0, false, 1, false, false, now(), now());
            """, ("p", promptId), ("s", space), ("u", user));

        var afterInsert = await CountSyncLogAsync(conn, promptId);

        // Chi bat has_conflict — khong duoc bump version, khong duoc them dong sync_log.
        await ExecAsync(conn, "UPDATE promptvault.prompts SET has_conflict = true WHERE prompt_id = @p;", ("p", promptId));
        Assert.Equal(1, await GetVersionAsync(conn, promptId));
        Assert.Equal(afterInsert, await CountSyncLogAsync(conn, promptId));

        // Chi bat is_favorite — cung khong duoc bump version, khong duoc them dong sync_log.
        await ExecAsync(conn, "UPDATE promptvault.prompts SET is_favorite = true WHERE prompt_id = @p;", ("p", promptId));
        Assert.Equal(1, await GetVersionAsync(conn, promptId));
        Assert.Equal(afterInsert, await CountSyncLogAsync(conn, promptId));
    }

    [Fact]
    public async Task Soft_delete_bump_version_va_ghi_sync_log_con_hard_delete_cung_duoc_ghi()
    {
        using var scope = _fixture.CreateScope();
        var db = ApiFixture.Db(scope);

        var (user, space) = await SeedUserAndSpaceAsync(db);
        var promptId = Guid.NewGuid();

        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();

        await ExecAsync(conn, """
            INSERT INTO promptvault.prompts (prompt_id, space_id, author_user_id, title, content,
                is_favorite, is_archived, usage_count, is_externalized, version, is_deleted,
                has_conflict, created_date, updated_date)
            VALUES (@p, @s, @u, 'T1', 'C1', false, false, 0, false, 1, false, false, now(), now());
            """, ("p", promptId), ("s", space), ("u", user));

        // P11: soft-delete (is_deleted flip) phai bump version giong mot content change that su.
        await ExecAsync(conn, "UPDATE promptvault.prompts SET is_deleted = true WHERE prompt_id = @p;", ("p", promptId));
        Assert.Equal(2, await GetVersionAsync(conn, promptId));
        Assert.Equal(2, await CountSyncLogAsync(conn, promptId));

        await ExecAsync(conn, "DELETE FROM promptvault.prompts WHERE prompt_id = @p;", ("p", promptId));
        Assert.Equal(3, await CountSyncLogAsync(conn, promptId));

        var ops = await GetOperationsAsync(conn, promptId);
        Assert.Equal(new[] { "insert", "update", "delete" }, ops);
    }

    private static async Task<(Guid userId, Guid spaceId)> SeedUserAndSpaceAsync(AioKinDbContext db)
    {
        var user = TestData.NewUser();
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var space = new Space
        {
            SpaceType = SpaceType.Team,
            Name = $"SyncTrigSpace-{Guid.NewGuid():N}",
            OwnerUserID = user.UserID
        };
        db.Spaces.Add(space);
        await db.SaveChangesAsync();

        return (user.UserID, space.SpaceID);
    }

    private static async Task ExecAsync(NpgsqlConnection conn, string sql, params (string name, object value)[] parameters)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<int> GetVersionAsync(NpgsqlConnection conn, Guid promptId)
    {
        await using var cmd = new NpgsqlCommand("SELECT version FROM promptvault.prompts WHERE prompt_id = @p;", conn);
        cmd.Parameters.AddWithValue("p", promptId);
        return (int)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task<long> CountSyncLogAsync(NpgsqlConnection conn, Guid entityId)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT count(*) FROM sync.sync_log WHERE entity_id = @e;", conn);
        cmd.Parameters.AddWithValue("e", entityId);
        return (long)(await cmd.ExecuteScalarAsync())!;
    }

    private static async Task<string[]> GetOperationsAsync(NpgsqlConnection conn, Guid entityId)
    {
        await using var cmd = new NpgsqlCommand(
            "SELECT operation FROM sync.sync_log WHERE entity_id = @e ORDER BY sync_log_id;", conn);
        cmd.Parameters.AddWithValue("e", entityId);
        await using var reader = await cmd.ExecuteReaderAsync();
        var ops = new List<string>();
        while (await reader.ReadAsync())
            ops.Add(reader.GetString(0));
        return ops.ToArray();
    }
}
