using AioKin.Data;
using AioKin.Models.ViewModel.Content;
using Microsoft.EntityFrameworkCore;

namespace AioKin.Services.Content;

public class DiscoveryService : IDiscoveryService
{
    private readonly AioKinDbContext _db;

    public DiscoveryService(AioKinDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<DiscoveryItemResponse>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        // AsNoTracking: doc thuan tuy, khong can change tracker giu lai entity.
        var items = await _db.DiscoveryItems
            .AsNoTracking()
            .Where(d => d.IsPublished)
            .OrderByDescending(d => d.CreatedDate)
            .ThenByDescending(d => d.DiscoveryItemID)
            .ToListAsync(cancellationToken);

        return [.. items.Select(DiscoveryItemResponse.From)];
    }

    public async Task<DiscoveryItemResponse?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        var item = await _db.DiscoveryItems
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.DiscoveryItemID == id && d.IsPublished, cancellationToken);

        return item is null ? null : DiscoveryItemResponse.From(item);
    }
}
