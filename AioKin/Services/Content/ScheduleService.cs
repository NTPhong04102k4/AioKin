using AioKin.Data;
using AioKin.Models.ViewModel.Content;
using Microsoft.EntityFrameworkCore;

namespace AioKin.Services.Content;

public class ScheduleService : IScheduleService
{
    private readonly AioKinDbContext _db;

    public ScheduleService(AioKinDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<ScheduleItemResponse>> GetForUserAsync(
        Guid userUuid,
        CancellationToken cancellationToken = default)
    {
        // Loc qua navigation property de chi mat mot lan tra database: token mang UserUUID
        // (id cong khai) con khoa ngoai tro toi UserID (id noi bo), nen doi chieu bang JOIN
        // thay vi tra bang users truoc roi tra tiep bang schedule_items.
        var items = await _db.ScheduleItems
            .AsNoTracking()
            .Where(s => s.User!.UserUUID == userUuid)
            .OrderBy(s => s.StartAt)
            .ThenBy(s => s.ScheduleItemID)
            .ToListAsync(cancellationToken);

        return [.. items.Select(ScheduleItemResponse.From)];
    }
}
