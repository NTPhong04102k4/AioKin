using AioKin.Common;
using AioKin.Data;
using AioKin.Data.Entities.Family;
using AioKin.Services.Common.Cache;
using Microsoft.EntityFrameworkCore;

namespace AioKin.Services.Family;

public class FamilyContext : IFamilyContext
{
    private readonly AioKinDbContext _db;
    private readonly IRedisService _redis;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public FamilyContext(
        AioKinDbContext db,
        IRedisService redis,
        IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _redis = redis;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<FamilyMembership?> ResolveAsync(
        Guid familyUuid,
        CancellationToken cancellationToken = default)
    {
        // Danh tinh LUON lay tu token. Khong co tham so nao cua ham nay noi nguoi goi la ai.
        var userUuid = _httpContextAccessor.HttpContext?.User.GetUserUuid();
        if (userUuid is null)
            return null;

        var cacheKey = RedisKeys.FamilyMembership(familyUuid, userUuid.Value);

        var cached = await _redis.GetAsync<CachedMembership>(cacheKey);
        if (cached is not null)
            return cached.ToMembership();

        // Mot lan tra database: JOIN qua navigation property thay vi tra bang users truoc.
        var row = await _db.FamilyMembers
            .AsNoTracking()
            .Where(m => m.IsActive
                     && m.User!.UserUUID == userUuid.Value
                     && m.Family!.FamilyUUID == familyUuid
                     && m.Family.IsActive)
            .Select(m => new CachedMembership
            {
                FamilyID = m.FamilyID,
                FamilyUUID = familyUuid,
                UserID = m.UserID,
                UserUUID = userUuid.Value,
                Role = m.MemberRole
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (row is null)
        {
            // Khong cache ket qua am. Nguoi vua duoc moi vao nha se phai doi het TTL moi vao
            // duoc, va ho se bao "app hong" — trong khi cai tiet kiem duoc chi la mot lan
            // truy van index.
            return null;
        }

        await _redis.SetAsync(cacheKey, row, RedisTtl.FamilyMembership);
        return row.ToMembership();
    }

    public Task InvalidateAsync(Guid familyUuid, Guid userUuid, CancellationToken cancellationToken = default)
        => _redis.DeleteAsync(RedisKeys.FamilyMembership(familyUuid, userUuid));

    public Task InvalidateFamilyAsync(Guid familyUuid, CancellationToken cancellationToken = default)
        => _redis.DeleteByPrefixAsync(RedisKeys.FamilyMembershipPrefix(familyUuid));

    /// <summary>
    /// Ban co the serialize duoc cua <see cref="FamilyMembership"/>. Record positional voi
    /// constructor bat buoc thi khong phai bo serializer nao cung dung lai duoc — mot class
    /// co property doc-ghi thi chac chan.
    /// </summary>
    private sealed class CachedMembership
    {
        public Guid FamilyID { get; set; }
        public Guid FamilyUUID { get; set; }
        public Guid UserID { get; set; }
        public Guid UserUUID { get; set; }
        public FamilyMemberRole Role { get; set; }

        public FamilyMembership ToMembership() => new(FamilyID, FamilyUUID, UserID, UserUUID, Role);
    }
}
